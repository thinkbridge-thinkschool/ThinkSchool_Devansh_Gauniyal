#!/bin/bash
# Drives the Day 21 Task 1 measurement end to end: builds the copied QuotesApi, starts
# it on a free local port against the real local Redis container, then runs bombardier
# (the same load-test tool day-11/task-1 and day-11/task-2 already use in this repo)
# against the uncached path and the cached path in turn, capturing:
#   - bombardier's own plain-text latency report (parsed by scripts/parse-measurement.cs,
#     which reuses day-11/task-1's LatencyPercentileParser.cs regex pattern verbatim)
#   - the real DB-query counter before/after each run
# into output/, plus the exact parameters used (output/params.json).
#
# Requires: a local Redis container reachable at the configured Redis:ConnectionString
# (see README.md "how I run it locally" for the docker run command), and `bombardier`
# on PATH. Re-runnable from a clean state; always stops the API afterwards, even on
# failure - same discipline as day-11/task-1/scripts/run-profile.sh.
#
# Usage: scripts/run-measurement.sh [concurrency] [duration]
#   concurrency (default 20): bombardier -c
#   duration    (default 10s): bombardier -d

set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="$TASK_ROOT/output"
QUOTESAPI_PROJECT_DIR="$TASK_ROOT/QuotesApi"
QUOTESAPI_DLL="$QUOTESAPI_PROJECT_DIR/bin/Debug/net10.0/QuotesApi.dll"

CONCURRENCY="${1:-20}"
DURATION="${2:-10s}"
PORT=5311
BASE_URL="http://localhost:$PORT"
DB_PATH="$OUTPUT_DIR/measurement-performance.db"
HEALTH_TIMEOUT_SECONDS=30
WALL_CLOCK_GUARD_SECONDS=180

SERVER_PID=""
WATCHDOG_PID=""

cleanup() {
    if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "Stopping API process (pid $SERVER_PID)..."
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
    if [ -n "$WATCHDOG_PID" ] && kill -0 "$WATCHDOG_PID" 2>/dev/null; then
        kill "$WATCHDOG_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT TERM

# Portable wall-clock guard: macOS has no `timeout` command (same workaround as
# day-11/task-1/scripts/run-profile.sh).
(
    sleep "$WALL_CLOCK_GUARD_SECONDS"
    echo "WATCHDOG: run-measurement.sh exceeded ${WALL_CLOCK_GUARD_SECONDS}s wall-clock guard, aborting." >&2
    kill -TERM "$$" 2>/dev/null || true
) &
WATCHDOG_PID=$!

if ! command -v bombardier >/dev/null 2>&1; then
    echo "bombardier is required (brew install bombardier) and was not found on PATH." >&2
    exit 1
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
rm -f "$DB_PATH" "$DB_PATH-shm" "$DB_PATH-wal"

echo "Building QuotesApi..."
dotnet build "$QUOTESAPI_PROJECT_DIR/QuotesApi.csproj" -c Debug >/dev/null

echo "Starting QuotesApi on $BASE_URL ..."
# Content root defaults to the process's current directory, not the DLL's own folder -
# running `dotnet $QUOTESAPI_DLL` from $TASK_ROOT (this script's cwd) silently failed to
# find appsettings.json, leaving Entra:TenantId empty and startup validation ("Entra
# tenant ID must be a valid GUID") fired on a missing value, not an invalid one.
# Verified the hard way: this script's first two runs both failed exactly this way,
# while starting the same DLL by hand from its own bin/ folder worked. cd'ing into the
# DLL's directory first fixes it - see PROVENANCE.md's verification log.
(
    cd "$(dirname "$QUOTESAPI_DLL")"
    PERFORMANCE_DB_PATH="$DB_PATH" \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="$BASE_URL" \
    InternalJwt__Issuer="measurement.local" \
    InternalJwt__Audience="measurement.local.clients" \
    InternalJwt__SigningKeyBase64="$(openssl rand -base64 32)" \
    InternalCaller__UserId="measurement-user" \
    InternalCaller__Email="measurement@example.test" \
    InternalCaller__PasswordSaltBase64="$(openssl rand -base64 16)" \
    InternalCaller__PasswordHashBase64="$(openssl rand -base64 32)" \
    exec dotnet "$QUOTESAPI_DLL"
) > "$OUTPUT_DIR/server.log" 2>&1 &
SERVER_PID=$!

echo "Waiting for health..."
elapsed=0
until curl -sf "$BASE_URL/" >/dev/null 2>&1; do
    sleep 1
    elapsed=$((elapsed + 1))
    if [ "$elapsed" -ge "$HEALTH_TIMEOUT_SECONDS" ]; then
        echo "API did not become healthy within ${HEALTH_TIMEOUT_SECONDS}s." >&2
        exit 1
    fi
done
echo "API is up (pid $SERVER_PID)."

echo "$CONCURRENCY $DURATION $(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$OUTPUT_DIR/params.txt"
cat > "$OUTPUT_DIR/params.json" << JSON
{
  "concurrency": $CONCURRENCY,
  "duration": "$DURATION",
  "startedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "baseUrl": "$BASE_URL"
}
JSON

echo "--- Uncached path ---"
curl -sf -X POST "$BASE_URL/api/measurement/reset" > /dev/null
bombardier -c "$CONCURRENCY" -d "$DURATION" -l "$BASE_URL/api/authors/quote-summary/uncached" \
    | tee "$OUTPUT_DIR/uncached-bombardier.txt"
curl -sf "$BASE_URL/api/measurement/db-query-count" > "$OUTPUT_DIR/uncached-db-queries.json"
cat "$OUTPUT_DIR/uncached-db-queries.json"
echo ""

echo "--- Cached path (starts on a genuinely cold key - reset evicts, it does not warm) ---"
# /api/measurement/reset evicts by tag, so there is deliberately no separate "warm up
# then reset" step here (an earlier version of this script did that, which was
# self-defeating: the reset after the warm-up read evicted the very entry it had just
# populated). Starting from reset means bombardier's own first wave of concurrent
# connections races on a cold key exactly like the dedicated xUnit stampede test -
# coalesces into one factory run - and every request after that for the rest of the
# window is a real cache hit. One bombardier run over one artificial-delay window
# demonstrates both stampede protection and steady-state hit rate.
curl -sf -X POST "$BASE_URL/api/measurement/reset" > /dev/null
bombardier -c "$CONCURRENCY" -d "$DURATION" -l "$BASE_URL/api/authors/quote-summary/cached?key=loadtest" \
    | tee "$OUTPUT_DIR/cached-bombardier.txt"
curl -sf "$BASE_URL/api/measurement/db-query-count" > "$OUTPUT_DIR/cached-db-queries.json"
cat "$OUTPUT_DIR/cached-db-queries.json"
echo ""

echo "Stopping API..."
kill "$SERVER_PID" 2>/dev/null || true
wait "$SERVER_PID" 2>/dev/null || true
SERVER_PID=""

echo "Parsing results (reusing day-11/task-1's LatencyPercentileParser pattern)..."
dotnet run "$TASK_ROOT/scripts/parse-measurement.cs" -- "$OUTPUT_DIR"

echo "Done. Raw output and summary.md are in $OUTPUT_DIR"
