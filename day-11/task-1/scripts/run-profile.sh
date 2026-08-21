#!/bin/bash
# Orchestrates the full Day 11 Task 1 measurement end to end: builds the API, starts it on
# a free local port, warms it up, runs the real load test with bombardier, captures a
# single request's SQL log, the EXPLAIN QUERY PLAN for the per-author query, and a dump of
# the Quotes table's indexes. Always stops the API afterwards, even on failure.
#
# Usage: scripts/run-profile.sh
# Re-runnable from a clean state with no manual steps.

set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="$TASK_ROOT/output"
DB_PATH="$OUTPUT_DIR/slowapi.db"
SLOWAPI_PROJECT_DIR="$TASK_ROOT/SlowApi"
SLOWAPI_DLL="$SLOWAPI_PROJECT_DIR/bin/Debug/net10.0/SlowApi.dll"

WARMUP_CONCURRENCY=10
WARMUP_DURATION="3s"
LOAD_CONCURRENCY=20
LOAD_DURATION="10s"
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

# Portable wall-clock guard: macOS has no `timeout` command. If the script hangs, this
# watchdog sends SIGTERM to this shell so `cleanup` still runs and no dotnet process is
# left holding the port.
(
    sleep "$WALL_CLOCK_GUARD_SECONDS"
    echo "WATCHDOG: run-profile.sh exceeded ${WALL_CLOCK_GUARD_SECONDS}s wall-clock guard, aborting." >&2
    kill -TERM "$$" 2>/dev/null || true
) &
WATCHDOG_PID=$!

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "=== Build ==="
dotnet build "$TASK_ROOT/Task1.slnx" --nologo -v minimal

echo "=== Pick a free port ==="
PORT=5187
while lsof -iTCP:"$PORT" -sTCP:LISTEN -P -n >/dev/null 2>&1; do
    PORT=$((PORT + 1))
done
BASE_URL="http://127.0.0.1:$PORT"
echo "Using port $PORT"

echo "=== Start API ==="
ASPNETCORE_URLS="$BASE_URL" SLOWAPI_DB_PATH="$DB_PATH" dotnet "$SLOWAPI_DLL" \
    > "$OUTPUT_DIR/server.log" 2>&1 &
SERVER_PID=$!

echo "Polling $BASE_URL/health until it responds (pid $SERVER_PID)..."
DEADLINE=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
until curl -sf "$BASE_URL/health" >/dev/null 2>&1; do
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "API process exited before becoming healthy. Server log:" >&2
        cat "$OUTPUT_DIR/server.log" >&2
        exit 1
    fi
    if [ "$SECONDS" -ge "$DEADLINE" ]; then
        echo "API did not become healthy within ${HEALTH_TIMEOUT_SECONDS}s." >&2
        exit 1
    fi
    sleep 0.5
done
echo "API is healthy."

echo "=== Warmup pass (discarded) ==="
bombardier -c "$WARMUP_CONCURRENCY" -d "$WARMUP_DURATION" "$BASE_URL/authors/quote-summary" \
    > /dev/null 2>&1 || true

echo "=== Real load test (captured) ==="
BOMBARDIER_CMD="bombardier -c $LOAD_CONCURRENCY -d $LOAD_DURATION -l $BASE_URL/authors/quote-summary"
{
    echo "Command: $BOMBARDIER_CMD"
    echo ""
} > "$OUTPUT_DIR/load-test.txt"
bombardier -c "$LOAD_CONCURRENCY" -d "$LOAD_DURATION" -l "$BASE_URL/authors/quote-summary" \
    | tee -a "$OUTPUT_DIR/load-test.txt"

echo "=== Single-request SQL log, query plan, schema dump ==="
dotnet "$SLOWAPI_DLL" diagnostics "$DB_PATH" "$OUTPUT_DIR"

echo "=== Environment info ==="
{
    echo "Load-test tool version:"
    bombardier --version 2>&1
    echo ""
    echo ".NET SDK version:"
    dotnet --version
    echo ""
    echo "Runtime identifier: osx-arm64"
    echo ""
    echo "Note: this is an Apple Silicon (arm64) laptop. The API and the load generator" \
         "ran on the same machine, competing for the same CPU cores. Absolute latency" \
         "numbers are therefore not comparable to a production measurement - only the" \
         "shape (p99 far above p50) is the finding."
} > "$OUTPUT_DIR/environment.txt"

echo "=== Done. Artefacts written to $OUTPUT_DIR ==="
