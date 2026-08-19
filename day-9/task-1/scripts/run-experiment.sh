#!/bin/bash
# Day 9 / Task 1 — orchestrates six two-session isolation-level experiments
# against SQL Server 2022 running in Docker (linux/amd64 under Rosetta
# emulation on Apple Silicon). See ../README.md for the full explanation of
# why named pipes are used instead of sleep/WAITFOR, and why a lock timeout
# counts as proof that an anomaly was prevented.
#
# Written for bash 3.2 (macOS's default /bin/bash) - no associative arrays,
# no `${var,,}`, no `{fd}>` dynamic descriptor allocation.
set -u

HERE="$(cd "$(dirname "$0")/.." && pwd)"
SQL_DIR="$HERE/sql"
OUT_DIR="$HERE/output"
TMP_DIR="$(mktemp -d /tmp/day9-isolation.XXXXXX)"
LOG_FILE="$OUT_DIR/run.log"

CONTAINER_NAME="day9-sql"
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
# Session helpers. Each call sends $BATCH (a global set by the caller) plus an
# appended PRINT marker, then either waits for that marker to land in the
# transcript or returns immediately (for a step expected to block).
# ---------------------------------------------------------------------------
send_and_wait() {
    fd=$1
    transcript=$2
    mark=$3
    max_seconds=$4
    printf '%s\n' "$BATCH" >&"$fd"
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

send_only() {
    fd=$1
    mark=$2
    printf '%s\n' "$BATCH" >&"$fd"
    printf "PRINT 'MARK:%s'\n" "$mark" >&"$fd"
    printf 'GO\n' >&"$fd"
}

# Like send_only, but also prints (and lets the caller wait for) a marker
# BEFORE the batch runs. Needed when a fire-and-forget statement on one
# session is immediately followed by a fire-and-forget statement on the
# OTHER session (e.g. the dirty-read attempt, then the rollback that may
# unblock it): without a confirmed "server has started this batch"
# checkpoint, the second session's independent connection can race ahead
# and finish before the first session's statement is even dispatched,
# since sqlcmd batches within one connection execute strictly in order but
# nothing orders two DIFFERENT connections relative to each other.
send_only_with_start_mark() {
    fd=$1
    start_mark=$2
    end_mark=$3
    printf "PRINT 'MARK:%s'\n" "$start_mark" >&"$fd"
    printf '%s\n' "$BATCH" >&"$fd"
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

# ---------------------------------------------------------------------------
# Open a two-session run: renders session A (verbatim) and session B (with
# __ISOLATION_LEVEL__ substituted) into the run's output directory, starts
# both long-lived sqlcmd sessions attached to fresh named pipes, and captures
# each session's real SPID.
# ---------------------------------------------------------------------------
open_run() {
    slug=$1
    tag=$2
    level=$3
    run_dir="$OUT_DIR/$slug/$tag"
    mkdir -p "$run_dir"

    templateA="$SQL_DIR/${slug}_sessionA.sql"
    templateB="$SQL_DIR/${slug}_sessionB.sql"
    renderedA="$run_dir/sessionA.rendered.sql"
    renderedB="$run_dir/sessionB.rendered.sql"
    cp "$templateA" "$renderedA"
    sed "s/__ISOLATION_LEVEL__/$level/" "$templateB" > "$renderedB"

    batchdirA="$TMP_DIR/${slug}_${tag}_A"
    batchdirB="$TMP_DIR/${slug}_${tag}_B"
    split_batches "$renderedA" "$batchdirA"
    split_batches "$renderedB" "$batchdirB"

    fifoA="$TMP_DIR/${slug}_${tag}_fifoA"
    fifoB="$TMP_DIR/${slug}_${tag}_fifoB"
    mkfifo "$fifoA" "$fifoB"

    transcriptA="$run_dir/sessionA.transcript.txt"
    transcriptB="$run_dir/sessionB.transcript.txt"
    : > "$transcriptA"
    : > "$transcriptB"

    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -d IsolationLab \
        < "$fifoA" > "$transcriptA" 2>&1 &
    PIDA=$!

    docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" "$CONTAINER_NAME" \
        "$SQLCMD_BIN" -S localhost -U sa -C -d IsolationLab \
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
    log "$slug/$tag: Session A SPID=$spidA, Session B SPID=$spidB"

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

batch_text() {
    cat "$1"
}

# ---------------------------------------------------------------------------
# Scenario: dirty read
# ---------------------------------------------------------------------------
run_dirty_read() {
    level=$1
    tag=$2
    slug="10_dirty_read"
    log "=== $slug ($tag): Session B isolation level = $level ==="
    reset_seed || return 1

    open_run "$slug" "$tag" "$level"

    # A: STEP 1 - begin transaction, update, leave uncommitted (LOCK_TIMEOUT
    # is set in this same batch - see the .sql file header for why).
    BATCH=$(batch_text "$batchdirA/batch_1.sql"); send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP1_UPDATE_DONE" 5

    # B: STEP 1 - set the isolation level under test.
    BATCH=$(batch_text "$batchdirB/batch_1.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_ISOLATION_SET" 5

    # B: STEP 2 - the dirty-read attempt. May block under READ COMMITTED, so
    # we cannot wait for it to finish here - but we DO wait for confirmation
    # that the server has started this batch (the marker printed before the
    # SELECT) before releasing A's rollback below. Without that checkpoint,
    # A's rollback runs on an independent connection and can race ahead,
    # completing before B's read is even dispatched - which would silently
    # turn the occurring-anomaly run into a non-anomaly (a bug caught by
    # testing, not assumed away).
    BATCH=$(batch_text "$batchdirB/batch_2.sql"); send_only_with_start_mark 4 "B_STEP2_READ_STARTING" "B_STEP2_READ_DONE"
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP2_READ_STARTING" 5

    # A: STEP 2 - roll back; this both discards the update and (if B is
    # blocked) releases the lock B's read is waiting on.
    BATCH=$(batch_text "$batchdirA/batch_2.sql"); send_and_wait 3 "$run_dir/sessionA.transcript.txt" "A_STEP2_ROLLBACK_DONE" 5

    # Now confirm B's STEP 2 actually completed (either immediately, or once
    # A's rollback released the lock it was blocked on).
    wait_for_mark "$run_dir/sessionB.transcript.txt" "B_STEP2_READ_DONE" 8

    # B: STEP 3 - read again, post-rollback.
    BATCH=$(batch_text "$batchdirB/batch_3.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP3_READ_DONE" 5

    close_run
    log "$slug ($tag) complete."
}

# ---------------------------------------------------------------------------
# Scenario: non-repeatable read
# ---------------------------------------------------------------------------
run_nonrepeatable() {
    level=$1
    tag=$2
    slug="11_nonrepeatable"
    log "=== $slug ($tag): Session B isolation level = $level ==="
    reset_seed || return 1

    open_run "$slug" "$tag" "$level"

    # B: STEP 1 - set the isolation level under test.
    BATCH=$(batch_text "$batchdirB/batch_1.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_ISOLATION_SET" 5

    # B: STEP 2 - begin transaction, first read.
    BATCH=$(batch_text "$batchdirB/batch_2.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP2_FIRST_READ_DONE" 5

    # A: STEP 1 - update + auto-commit (LOCK_TIMEOUT is set in this same
    # batch - see the .sql file header for why). May block under REPEATABLE
    # READ until B commits, or may time out (error 1222) if B commits too
    # late. Do not wait here.
    BATCH=$(batch_text "$batchdirA/batch_1.sql"); send_only 3 "A_STEP1_UPDATE_DONE"

    # Give A a real chance to either succeed or hit its own 5s LOCK_TIMEOUT
    # before B reads again or commits, so a genuine timeout is observed
    # rather than short-circuited by an early commit.
    wait_for_mark "$run_dir/sessionA.transcript.txt" "A_STEP1_UPDATE_DONE" 7

    # B: STEP 3 - second read, same transaction.
    BATCH=$(batch_text "$batchdirB/batch_3.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP3_SECOND_READ_DONE" 5

    # B: STEP 4 - commit.
    BATCH=$(batch_text "$batchdirB/batch_4.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP4_COMMIT_DONE" 5

    close_run
    log "$slug ($tag) complete."
}

# ---------------------------------------------------------------------------
# Scenario: phantom read
# ---------------------------------------------------------------------------
run_phantom() {
    level=$1
    tag=$2
    slug="12_phantom"
    log "=== $slug ($tag): Session B isolation level = $level ==="
    reset_seed || return 1

    open_run "$slug" "$tag" "$level"

    # B: STEP 1 - set the isolation level under test.
    BATCH=$(batch_text "$batchdirB/batch_1.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_ISOLATION_SET" 5

    # B: STEP 2 - begin transaction, first range read.
    BATCH=$(batch_text "$batchdirB/batch_2.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP2_FIRST_RANGE_DONE" 5

    # A: STEP 1 - insert a row inside the range + auto-commit (LOCK_TIMEOUT is
    # set in this same batch - see the .sql file header for why). May block
    # under SERIALIZABLE until B commits, or time out (error 1222). Do not
    # wait here.
    BATCH=$(batch_text "$batchdirA/batch_1.sql"); send_only 3 "A_STEP1_INSERT_DONE"

    # Give A a real chance to either succeed or hit its own 5s LOCK_TIMEOUT.
    wait_for_mark "$run_dir/sessionA.transcript.txt" "A_STEP1_INSERT_DONE" 7

    # B: STEP 3 - second range read, same transaction.
    BATCH=$(batch_text "$batchdirB/batch_3.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP3_SECOND_RANGE_DONE" 5

    # B: STEP 4 - commit.
    BATCH=$(batch_text "$batchdirB/batch_4.sql"); send_and_wait 4 "$run_dir/sessionB.transcript.txt" "B_STEP4_COMMIT_DONE" 5

    close_run
    log "$slug ($tag) complete."
}

main() {
    SA_PASSWORD=$(generate_sa_password)

    start_or_recreate_container || exit 1

    log "Creating IsolationLab database..."
    sqlcmd_exec < "$SQL_DIR/00_create_database.sql" > "$TMP_DIR/00.out" 2>&1
    cat "$TMP_DIR/00.out" >> "$LOG_FILE"

    log "Creating dbo.Accounts schema..."
    sqlcmd_exec < "$SQL_DIR/01_schema.sql" > "$TMP_DIR/01.out" 2>&1
    cat "$TMP_DIR/01.out" >> "$LOG_FILE"

    log "Recording snapshot-isolation settings..."
    sqlcmd_exec < "$SQL_DIR/03_verify_snapshot_off.sql" > "$OUT_DIR/00_snapshot_settings.txt" 2>&1
    cat "$OUT_DIR/00_snapshot_settings.txt" >> "$LOG_FILE"
    if grep -qE '\b1\b.*ON|ON.*\b1\b' "$OUT_DIR/00_snapshot_settings.txt"; then
        : # handled by explicit column check below
    fi
    if ! grep -q "OFF" "$OUT_DIR/00_snapshot_settings.txt"; then
        log "ERROR: could not confirm snapshot settings are OFF - see $OUT_DIR/00_snapshot_settings.txt. Stopping."
        exit 1
    fi
    if grep -Eq '^\s*IsolationLab\s+1\s' "$OUT_DIR/00_snapshot_settings.txt"; then
        log "ERROR: READ_COMMITTED_SNAPSHOT is ON for IsolationLab. Stopping per instructions."
        exit 1
    fi

    log "Seeding initial data..."
    reset_seed || exit 1

    run_dirty_read "READ UNCOMMITTED" "occurs_READ_UNCOMMITTED"
    run_dirty_read "READ COMMITTED" "prevented_READ_COMMITTED"

    run_nonrepeatable "READ COMMITTED" "occurs_READ_COMMITTED"
    run_nonrepeatable "REPEATABLE READ" "prevented_REPEATABLE_READ"

    run_phantom "REPEATABLE READ" "occurs_REPEATABLE_READ"
    run_phantom "SERIALIZABLE" "prevented_SERIALIZABLE"

    log "All six scenarios complete."

    if [ "$STOP_CONTAINER_AT_END" = "1" ]; then
        log "Stopping and removing $CONTAINER_NAME."
        docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1
    else
        log "Leaving $CONTAINER_NAME running, as configured."
    fi
}

run_with_wall_clock_guard "$MAX_WALL_SECONDS" main
exit $?
