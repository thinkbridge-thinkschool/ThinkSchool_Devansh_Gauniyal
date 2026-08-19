#!/bin/bash
# Day 9 / Task 2 — forces a classic two-resource deadlock across two
# sqlcmd sessions against SQL Server 2022 running in Docker (linux/amd64
# under Rosetta emulation on Apple Silicon), captures the deadlock graph two
# independent ways, then re-runs the same interleaving with consistent lock
# ordering to show it no longer deadlocks. See ../README.md for the full
# explanation of the interleaving, why the two blocking statements must be
# fired without waiting on each other, and why LOCK_TIMEOUT must not be set.
#
# Written for bash 3.2 (macOS's default /bin/bash) - no associative arrays,
# no `${var,,}`, no `{fd}>` dynamic descriptor allocation.
set -u

HERE="$(cd "$(dirname "$0")/.." && pwd)"
SQL_DIR="$HERE/sql"
OUT_DIR="$HERE/output"
TMP_DIR="$(mktemp -d /tmp/day9-deadlock.XXXXXX)"
LOG_FILE="$OUT_DIR/run.log"

CONTAINER_NAME="day9-deadlock-sql"
IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
SQLCMD_BIN="/opt/mssql-tools18/bin/sqlcmd"
MAX_WALL_SECONDS=900
STOP_CONTAINER_AT_END=1

mkdir -p "$OUT_DIR"
: > "$LOG_FILE"

log() {
    # Writes to stderr (visible in the terminal) and the log file, never to
    # stdout - several functions below (e.g. open_run) return values via
    # stdout through command substitution, and log output must not pollute
    # that channel.
    printf '%s %s\n' "$(date '+%H:%M:%S')" "$1" | tee -a "$LOG_FILE" >&2
}

cleanup_tmp() {
    rm -rf "$TMP_DIR"
}
trap cleanup_tmp EXIT

# ---------------------------------------------------------------------------
# Portable wall-clock guard (macOS has no `timeout` by default).
# ---------------------------------------------------------------------------
run_with_wall_clock_guard() {
    seconds=$1
    shift
    "$@" &
    cmd_pid=$!
    (
        sleep "$seconds"
        if kill -0 "$cmd_pid" 2>/dev/null; then
            echo "$(date '+%H:%M:%S') WALL-CLOCK GUARD FIRED: killing wedged run (pid $cmd_pid) after ${seconds}s" >> "$LOG_FILE"
            kill -TERM "$cmd_pid" 2>/dev/null
            sleep 3
            kill -KILL "$cmd_pid" 2>/dev/null
            docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1
        fi
    ) &
    watchdog_pid=$!
    wait "$cmd_pid"
    status=$?
    kill "$watchdog_pid" 2>/dev/null
    wait "$watchdog_pid" 2>/dev/null
    return $status
}

# ---------------------------------------------------------------------------
# SA password: generated at runtime, held only in this shell variable, never
# printed, never written to any file, never used in a filename.
# ---------------------------------------------------------------------------
generate_sa_password() {
    rand_part=$(openssl rand -base64 32 | tr -dc 'A-Za-z0-9' | cut -c1-20)
    echo "Aa1!${rand_part}"
}

find_free_port() {
    port=1434
    while docker ps --format '{{.Ports}}' 2>/dev/null | grep -q ":${port}->" \
        || nc -z localhost "$port" 2>/dev/null; do
        port=$((port + 1))
    done
    echo "$port"
}

sqlcmd_exec() {
    # One-shot batch execution via docker exec, piping stdin. No password on argv.
    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -b
}

sqlcmd_capture_exec() {
    # Like sqlcmd_exec, but with output truncation disabled (-y 0): the
    # deadlock graph XML and error-log lines are far longer than sqlcmd's
    # default 256-character display width for large-value types. This
    # mssql-tools18 build of sqlcmd rejects -y combined with either -h -1
    # or -W ("mutually exclusive"), so headers and trailing whitespace are
    # left in and stripped downstream by the tag-based extraction (which
    # only starts capturing at the first matching XML tag, so header/footer
    # noise is never included in the saved file anyway).
    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -y 0
}

wait_for_server_ready() {
    attempts=0
    max_attempts=60
    while [ $attempts -lt $max_attempts ]; do
        if docker exec -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
            "$SQLCMD_BIN" -S localhost -U sa -C -Q "SELECT 1" >/dev/null 2>&1; then
            return 0
        fi
        attempts=$((attempts + 1))
        sleep 2
    done
    return 1
}

start_or_recreate_container() {
    if docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; then
        log "Removing stale $CONTAINER_NAME container from a previous invocation (its SA password is unknown to this run, by design - it is never persisted)."
        docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1
    fi

    HOST_PORT=$(find_free_port)
    log "Using host port $HOST_PORT for $CONTAINER_NAME."

    ENV_FILE="$TMP_DIR/mssql.env"
    old_umask=$(umask)
    umask 177
    {
        echo "ACCEPT_EULA=Y"
        echo "MSSQL_SA_PASSWORD=$SA_PASSWORD"
        echo "MSSQL_PID=Developer"
    } > "$ENV_FILE"
    umask "$old_umask"

    docker run -d --name "$CONTAINER_NAME" \
        --platform linux/amd64 \
        --env-file "$ENV_FILE" \
        -p "${HOST_PORT}:1433" \
        "$IMAGE" >/dev/null
    rm -f "$ENV_FILE"

    log "Waiting for SQL Server to accept connections..."
    if ! wait_for_server_ready; then
        log "ERROR: SQL Server did not become ready in time."
        return 1
    fi
    log "SQL Server is ready (container $CONTAINER_NAME, host port $HOST_PORT)."
    return 0
}

# ---------------------------------------------------------------------------
# Batch splitting: split a rendered .sql file into one file per batch,
# splitting on lines that are exactly "GO". Comments stay attached to the
# batch they precede, so the orchestrator sends the exact documented text.
# ---------------------------------------------------------------------------
split_batches() {
    file=$1
    outdir=$2
    mkdir -p "$outdir"
    rm -f "$outdir"/batch_*.sql
    awk -v outdir="$outdir" '
        BEGIN { n = 0; buf = "" }
        {
            line = $0
            gsub(/\r$/, "", line)
            if (line ~ /^[ \t]*GO[ \t]*$/) {
                n++
                outfile = outdir "/batch_" n ".sql"
                printf "%s", buf > outfile
                close(outfile)
                buf = ""
            } else {
                buf = buf line "\n"
            }
        }
    ' "$file"
}

# ---------------------------------------------------------------------------
# Session helpers. Each call sends $BATCH (a global set by the caller) plus
# an appended PRINT marker, then either waits for that marker to land in the
# transcript or returns immediately (for a step expected to block).
# ---------------------------------------------------------------------------
send_and_wait() {
    fd=$1
    transcript=$2
    mark=$3
    max_seconds=$4
    # BATCH and its completion marker are sent as two SEPARATE GO batches,
    # not one. sqlcmd only writes a batch's output to the transcript once
    # that whole batch has finished, so a marker appended to the SAME batch
    # as a statement that errors (e.g. a COMMIT with no matching BEGIN,
    # which can happen here when a session was the deadlock victim) may
    # never print at all - the error aborts the rest of that batch. Sending
    # the marker as its own follow-up batch means it always prints once
    # sqlcmd has moved on to it, regardless of how the preceding statement
    # finished, as long as the connection itself is still alive.
    printf '%s\n' "$BATCH" >&"$fd"
    printf 'GO\n' >&"$fd"
    printf "PRINT 'MARK:%s'\n" "$mark" >&"$fd"
    printf 'GO\n' >&"$fd"
    ticks=0
    max_ticks=$((max_seconds * 5))
    while ! grep -q "MARK:$mark" "$transcript" 2>/dev/null; do
        sleep 0.2
        ticks=$((ticks + 1))
        if [ $ticks -ge $max_ticks ]; then
            log "WARN: timed out waiting for marker $mark after ${max_seconds}s"
            return 1
        fi
    done
    return 0
}

# Fire-and-forget: sends a start marker, then the batch, then an end marker
# - each as its OWN separate GO batch - without waiting for any of them.
# Used for the two statements that request the other session's already-held
# resource: one of these two will block until the deadlock monitor resolves
# the cycle, so the orchestrator must not wait here or it would itself hang
# on a statement that can never return on its own.
#
# The three are separate batches (not one combined batch) for two reasons:
# sqlcmd only flushes a batch's output once that whole batch finishes, so
# (a) a start marker sent as part of the SAME batch as BATCH would not
# appear until BATCH itself finished - defeating its purpose as an early
# dispatch confirmation - and (b) if BATCH errors (e.g. this session is
# chosen as the deadlock victim), a marker in the same batch would never
# print at all, since the error aborts the rest of that batch. As separate
# batches, sqlcmd processes them strictly in order over this one
# connection, so the start marker reliably flushes before BATCH is even
# sent, and the end marker reliably flushes once BATCH's batch is done,
# whether BATCH succeeded or failed.
send_only_with_start_mark() {
    fd=$1
    start_mark=$2
    end_mark=$3
    printf "PRINT 'MARK:%s'\n" "$start_mark" >&"$fd"
    printf 'GO\n' >&"$fd"
    printf '%s\n' "$BATCH" >&"$fd"
    printf 'GO\n' >&"$fd"
    printf "PRINT 'MARK:%s'\n" "$end_mark" >&"$fd"
    printf 'GO\n' >&"$fd"
}

wait_for_mark() {
    transcript=$1
    mark=$2
    max_seconds=$3
    ticks=0
    max_ticks=$((max_seconds * 5))
    while ! grep -q "MARK:$mark" "$transcript" 2>/dev/null; do
        sleep 0.2
        ticks=$((ticks + 1))
        if [ $ticks -ge $max_ticks ]; then
            log "WARN: timed out waiting for marker $mark after ${max_seconds}s"
            return 1
        fi
    done
    return 0
}

reset_seed() {
    sqlcmd_exec < "$SQL_DIR/02_seed.sql" > "$TMP_DIR/reset_seed.out" 2>&1
    if ! grep -qi "error" "$TMP_DIR/reset_seed.out"; then
        return 0
    fi
    log "ERROR resetting seed data:"
    cat "$TMP_DIR/reset_seed.out" | tee -a "$LOG_FILE"
    return 1
}

batch_text() {
    cat "$1"
}

# ---------------------------------------------------------------------------
# Open a two-session run: copies both session scripts verbatim into the
# run's output directory (nothing is templated in this experiment - unlike
# Day 9 / Task 1, there is no isolation-level placeholder), starts both
# long-lived sqlcmd sessions attached to fresh named pipes, and captures
# each session's real SPID.
# ---------------------------------------------------------------------------
open_run() {
    dir_name=$1
    templateA=$2
    templateB=$3
    run_dir="$OUT_DIR/$dir_name"
    mkdir -p "$run_dir"

    renderedA="$run_dir/sessionA.rendered.sql"
    renderedB="$run_dir/sessionB.rendered.sql"
    cp "$SQL_DIR/$templateA" "$renderedA"
    cp "$SQL_DIR/$templateB" "$renderedB"

    batchdirA="$TMP_DIR/${dir_name}_A"
    batchdirB="$TMP_DIR/${dir_name}_B"
    split_batches "$renderedA" "$batchdirA"
    split_batches "$renderedB" "$batchdirB"

    fifoA="$TMP_DIR/${dir_name}_fifoA"
    fifoB="$TMP_DIR/${dir_name}_fifoB"
    mkfifo "$fifoA" "$fifoB"

    transcriptA="$run_dir/sessionA.transcript.txt"
    transcriptB="$run_dir/sessionB.transcript.txt"
    : > "$transcriptA"
    : > "$transcriptB"

    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -d DeadlockLab \
        < "$fifoA" > "$transcriptA" 2>&1 &
    PIDA=$!

    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -d DeadlockLab \
        < "$fifoB" > "$transcriptB" 2>&1 &
    PIDB=$!

    exec 3>"$fifoA"
    exec 4>"$fifoB"

    BATCH="PRINT 'SPID:' + CONVERT(varchar(20), @@SPID);"
    send_and_wait 3 "$transcriptA" "SPID_A_CAPTURED" 10
    BATCH="PRINT 'SPID:' + CONVERT(varchar(20), @@SPID);"
    send_and_wait 4 "$transcriptB" "SPID_B_CAPTURED" 10

    spidA=$(grep -o 'SPID:[0-9]*' "$transcriptA" | head -1 | cut -d: -f2)
    spidB=$(grep -o 'SPID:[0-9]*' "$transcriptB" | head -1 | cut -d: -f2)
    {
        echo "Session A SPID: $spidA"
        echo "Session B SPID: $spidB"
        if [ "$spidA" = "$spidB" ] || [ -z "$spidA" ] || [ -z "$spidB" ]; then
            echo "WARNING: SPIDs are not two distinct non-empty values."
        else
            echo "Confirmed: two distinct SQL Server sessions."
        fi
    } > "$run_dir/spids.txt"
    log "$dir_name: Session A SPID=$spidA, Session B SPID=$spidB"

    # Deliberately NOT invoked via $(...): exec 3>/4> above must open file
    # descriptors in THIS shell, not a subshell, or they vanish before the
    # caller can use them. batchdirA/batchdirB/run_dir/PIDA/PIDB are left as
    # plain (global) variables for the caller to read directly.
}

close_run() {
    # The 2>/dev/null must be scoped to a brace group, not attached to a bare
    # `exec` - `exec 3>&- 2>/dev/null` would permanently redirect this
    # script's own stderr (including every later log() call) for the rest of
    # its life, since `exec` with no command applies redirections to the
    # current shell.
    { exec 3>&-; } 2>/dev/null
    { exec 4>&-; } 2>/dev/null
    wait "$PIDA" 2>/dev/null
    wait "$PIDB" 2>/dev/null
}

# ---------------------------------------------------------------------------
# Scenario: BROKEN lock ordering (A: Accounts then Orders; B: Orders then
# Accounts). SESSION A step 1 locks Accounts, SESSION B step 1 locks Orders;
# then BOTH sessions request the other's resource. The two blocking requests
# are fired without waiting on each other - see send_only_with_start_mark -
# since waiting for either to return before firing the other would hang this
# orchestrator forever on a statement that cannot complete until the
# deadlock monitor breaks the cycle.
# ---------------------------------------------------------------------------
run_broken_deadlock() {
    log "=== 10_deadlock (broken ordering): forcing a circular wait ==="
    reset_seed || return 1

    open_run "10_deadlock_broken" "10_deadlock_sessionA.sql" "11_deadlock_sessionB.sql"

    log "SESSION A step 1 - lock Accounts row Id=1"
    BATCH=$(batch_text "$batchdirA/batch_1.sql")
    send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP1_LOCK_ACCOUNTS_DONE" 10 || return 1

    log "SESSION B step 1 - lock Orders row Id=1"
    BATCH=$(batch_text "$batchdirB/batch_1.sql")
    send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP1_LOCK_ORDERS_DONE" 10 || return 1

    log "SESSION A step 2 - request Orders row Id=1 (held by B) - fired without waiting"
    BATCH=$(batch_text "$batchdirA/batch_2.sql")
    send_only_with_start_mark 3 "A_STEP2_STARTING" "A_STEP2_DONE"
    wait_for_mark "$run_dir/sessionA.transcript.txt" "A_STEP2_STARTING" 10

    log "SESSION B step 2 - request Accounts row Id=1 (held by A) - fired without waiting"
    BATCH=$(batch_text "$batchdirB/batch_2.sql")
    send_only_with_start_mark 4 "B_STEP2_STARTING" "B_STEP2_DONE"
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP2_STARTING" 10

    log "Both sessions are now in a circular wait; waiting for SQL Server's deadlock monitor to resolve it..."
    wait_for_mark "$run_dir/sessionA.transcript.txt" "A_STEP2_DONE" 60
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP2_DONE" 60

    log "SESSION A step 3 - commit attempt (succeeds if A survived, errors harmlessly if A was the victim)"
    BATCH=$(batch_text "$batchdirA/batch_3.sql")
    send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP3_COMMIT_DONE" 10

    log "SESSION B step 3 - commit attempt (succeeds if B survived, errors harmlessly if B was the victim)"
    BATCH=$(batch_text "$batchdirB/batch_3.sql")
    send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP3_COMMIT_DONE" 10

    close_run

    a_has_1205=0
    b_has_1205=0
    grep -q "1205" "$run_dir/sessionA.transcript.txt" && a_has_1205=1
    grep -q "1205" "$run_dir/sessionB.transcript.txt" && b_has_1205=1

    {
        echo "Session A saw error 1205: $a_has_1205"
        echo "Session B saw error 1205: $b_has_1205"
        if [ "$a_has_1205" = "1" ] && [ "$b_has_1205" = "0" ]; then
            echo "Victim: Session A (SPID $spidA)"
        elif [ "$b_has_1205" = "1" ] && [ "$a_has_1205" = "0" ]; then
            echo "Victim: Session B (SPID $spidB)"
        else
            echo "UNEXPECTED: expected exactly one session to report error 1205."
        fi
        echo "Victim selection is not deterministic - SQL Server picks by estimated rollback cost, so a re-run may pick the other session."
    } > "$run_dir/victim.txt"
    cat "$run_dir/victim.txt" | tee -a "$LOG_FILE"

    log "10_deadlock (broken ordering) complete."
}

# ---------------------------------------------------------------------------
# Scenario: FIXED lock ordering (both A and B: Accounts then Orders). B's
# first step now targets the SAME resource A already holds, so it simply
# queues behind A (ordinary blocking, fired without waiting since we cannot
# know in advance how long that wait will be) rather than being able to grab
# an independent resource first - no circular wait can form.
# ---------------------------------------------------------------------------
run_fixed_ordering() {
    log "=== 20_fixed (consistent ordering): same interleaving attempt, no cycle possible ==="
    reset_seed || return 1

    open_run "20_fixed" "20_fixed_sessionA.sql" "21_fixed_sessionB.sql"

    log "SESSION A step 1 - lock Accounts row Id=1"
    BATCH=$(batch_text "$batchdirA/batch_1.sql")
    send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP1_LOCK_ACCOUNTS_DONE" 10 || return 1

    log "SESSION B step 1 - request Accounts row Id=1 (held by A) - fired without waiting, since this now queues rather than deadlocking"
    BATCH=$(batch_text "$batchdirB/batch_1.sql")
    send_only_with_start_mark 4 "B_STEP1_STARTING" "B_STEP1_DONE"
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP1_STARTING" 10

    log "SESSION A step 2 - lock Orders row Id=1 (uncontested at this point)"
    BATCH=$(batch_text "$batchdirA/batch_2.sql")
    send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP2_LOCK_ORDERS_DONE" 10 || return 1

    log "SESSION A step 3 - commit, releasing both locks"
    BATCH=$(batch_text "$batchdirA/batch_3.sql")
    send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP3_COMMIT_DONE" 10 || return 1

    log "Waiting for SESSION B's queued request to unblock now that A has committed..."
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP1_DONE" 30 || return 1

    log "SESSION B step 2 - lock Orders row Id=1 (A already released it)"
    BATCH=$(batch_text "$batchdirB/batch_2.sql")
    send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP2_LOCK_ORDERS_DONE" 10 || return 1

    log "SESSION B step 3 - commit"
    BATCH=$(batch_text "$batchdirB/batch_3.sql")
    send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP3_COMMIT_DONE" 10 || return 1

    close_run

    if grep -q "1205" "$run_dir/sessionA.transcript.txt" "$run_dir/sessionB.transcript.txt"; then
        log "UNEXPECTED: error 1205 appeared in the fixed run - the fix did not work as intended."
    else
        log "Confirmed: no error 1205 in either transcript. Both sessions completed."
    fi

    log "20_fixed (consistent ordering) complete."
}

# ---------------------------------------------------------------------------
# Deadlock graph capture, route (a): system_health Extended Events ring
# buffer. Independent of trace flag 1222 - this always runs regardless of
# whether the flag is on.
# ---------------------------------------------------------------------------
capture_xevents_deadlock_graph() {
    log "Capturing deadlock graph from the system_health Extended Events ring buffer..."
    sqlcmd_capture_exec < "$SQL_DIR/30_capture_deadlock_xevents.sql" > "$TMP_DIR/xevents_raw.txt" 2>&1
    cp "$TMP_DIR/xevents_raw.txt" "$OUT_DIR/xevents_deadlock_capture.raw.txt"

    # Trim to just the well-formed <event ...>...</event> element - sqlcmd's
    # -h -1 leaves no header, but may leave a trailing blank line or a
    # "(1 rows affected)" style footer depending on server locale.
    awk '/<event /{p=1} p{print} /<\/event>/{if (p) exit}' "$TMP_DIR/xevents_raw.txt" > "$OUT_DIR/deadlock_graph.xdl"

    if [ -s "$OUT_DIR/deadlock_graph.xdl" ]; then
        log "Saved deadlock graph XML to output/deadlock_graph.xdl ($(wc -l < "$OUT_DIR/deadlock_graph.xdl" | tr -d ' ') lines)."
    else
        log "WARNING: no <event> element found in the Extended Events capture - see output/xevents_deadlock_capture.raw.txt for the real output."
    fi
}

# ---------------------------------------------------------------------------
# Deadlock graph capture, route (b): trace flag 1222 deadlock report text
# out of the SQL Server error log.
# ---------------------------------------------------------------------------
capture_errorlog_deadlock_report() {
    log "Capturing the trace flag 1222 deadlock report from the SQL Server error log..."
    sqlcmd_capture_exec < "$SQL_DIR/31_capture_deadlock_errorlog.sql" > "$TMP_DIR/errorlog_raw.txt" 2>&1
    cp "$TMP_DIR/errorlog_raw.txt" "$OUT_DIR/errorlog_deadlock_capture.raw.txt"

    # Trace flag 1222's error-log report is NOT angle-bracket XML - SQL
    # Server flattens the same element/attribute structure into indented
    # plain text, one log line per element, each prefixed with a timestamp
    # and the reporting SPID (observed in a real capture as e.g.
    # "2026-08-19 09:15:03.590 spid45s deadlock-list"). The block runs from
    # the "deadlock-list" line through every subsequent line that either
    # carries that SAME spid token or has no timestamp prefix at all (a
    # wrapped continuation line, e.g. the embedded inputbuf SQL text) - it
    # ends at the next timestamped line for a DIFFERENT spid.
    awk '
        BEGIN { started = 0; spidtok = "" }
        !started && /deadlock-list/ {
            started = 1
            if (match($0, /spid[0-9]+s/)) spidtok = substr($0, RSTART, RLENGTH)
            print
            next
        }
        started {
            if ($0 ~ /^[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] /) {
                if (spidtok != "" && index($0, spidtok) == 0) exit
            }
            print
        }
    ' "$TMP_DIR/errorlog_raw.txt" > "$OUT_DIR/errorlog_deadlock_report.txt"

    if [ -s "$OUT_DIR/errorlog_deadlock_report.txt" ]; then
        log "Saved trace flag 1222 deadlock report to output/errorlog_deadlock_report.txt ($(wc -l < "$OUT_DIR/errorlog_deadlock_report.txt" | tr -d ' ') lines)."
    else
        log "WARNING: no <deadlock-list> block found in the error log capture - see output/errorlog_deadlock_capture.raw.txt for the real output."
    fi
}

main() {
    SA_PASSWORD=$(generate_sa_password)

    start_or_recreate_container || exit 1

    log "Creating DeadlockLab database..."
    sqlcmd_exec < "$SQL_DIR/00_create_database.sql" > "$TMP_DIR/00.out" 2>&1
    cat "$TMP_DIR/00.out" >> "$LOG_FILE"

    log "Creating dbo.Accounts and dbo.Orders schema..."
    sqlcmd_exec < "$SQL_DIR/01_schema.sql" > "$TMP_DIR/01.out" 2>&1
    cat "$TMP_DIR/01.out" >> "$LOG_FILE"

    log "Seeding initial data..."
    reset_seed || exit 1

    log "Enabling trace flag 1222..."
    sqlcmd_exec < "$SQL_DIR/03_enable_traceflag_1222.sql" > "$OUT_DIR/traceflag_1222_enabled.txt" 2>&1
    cat "$OUT_DIR/traceflag_1222_enabled.txt" >> "$LOG_FILE"
    # DBCC TRACESTATUS(1222) (a specific flag number, not -1) always returns
    # exactly one row for that flag, showing Status 0 or 1 - unlike
    # TRACESTATUS(-1), it is never simply empty when the flag is off. So the
    # check must read the Status column, not just look for the literal
    # string "1222" (which is present in the TraceFlag column either way).
    if ! grep -Eq '^[[:space:]]*1222[[:space:]]+1[[:space:]]' "$OUT_DIR/traceflag_1222_enabled.txt"; then
        log "ERROR: could not confirm trace flag 1222 is ON - see output/traceflag_1222_enabled.txt. Stopping."
        exit 1
    fi

    run_broken_deadlock || log "WARNING: run_broken_deadlock did not complete as expected - see run.log and the captured transcripts above for the real outcome."

    capture_xevents_deadlock_graph
    capture_errorlog_deadlock_report

    log "Disabling trace flag 1222..."
    sqlcmd_exec < "$SQL_DIR/04_disable_traceflag_1222.sql" > "$OUT_DIR/traceflag_1222_disabled.txt" 2>&1
    cat "$OUT_DIR/traceflag_1222_disabled.txt" >> "$LOG_FILE"
    if grep -Eq '^[[:space:]]*1222[[:space:]]+1[[:space:]]' "$OUT_DIR/traceflag_1222_disabled.txt"; then
        log "ERROR: trace flag 1222 still appears ON after TRACEOFF - see output/traceflag_1222_disabled.txt."
    else
        log "Confirmed: trace flag 1222 is OFF (DBCC TRACESTATUS shows Status 0)."
    fi

    log "Resetting seed data before the fixed-ordering run..."
    reset_seed || exit 1

    run_fixed_ordering || log "WARNING: run_fixed_ordering did not complete as expected - see run.log and the captured transcripts above for the real outcome."

    log "Both scenarios complete."

    if [ "$STOP_CONTAINER_AT_END" = "1" ]; then
        log "Stopping and removing $CONTAINER_NAME."
        docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1
    else
        log "Leaving $CONTAINER_NAME running, as configured."
    fi
}

run_with_wall_clock_guard "$MAX_WALL_SECONDS" main
exit $?
