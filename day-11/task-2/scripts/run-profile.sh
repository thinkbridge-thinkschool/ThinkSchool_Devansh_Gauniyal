#!/bin/bash
# Orchestrates the full Day 11 Task 2 re-measurement end to end: builds the fixed API,
# starts it on a free local port, warms it up, runs the real load test against BOTH fixed
# variants with parameters recovered verbatim from task-1, captures a single request's SQL
# log and EXPLAIN QUERY PLAN for each variant, and dumps the schema showing the index on
# Quote.AuthorId now exists. Always stops the API afterwards, even on failure.
#
# Usage: scripts/run-profile.sh
# Re-runnable from a clean state with no manual steps.

set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TASK1_OUTPUT_DIR="$TASK_ROOT/../task-1/output"
OUTPUT_DIR="$TASK_ROOT/output"
DB_PATH="$OUTPUT_DIR/fastapi.db"
FASTAPI_PROJECT_DIR="$TASK_ROOT/FastApi"
FASTAPI_DLL="$FASTAPI_PROJECT_DIR/bin/Debug/net10.0/FastApi.dll"

# Parameters recovered verbatim from task-1's committed output/load-test.txt and
# output/environment.txt. Asserted against those files below, failing loudly on drift,
# rather than just trusted.
EXPECTED_CONCURRENCY=20
EXPECTED_DURATION="10s"
EXPECTED_TOOL_VERSION_PREFIX="bombardier version"

WARMUP_CONCURRENCY=10
WARMUP_DURATION="3s"
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

# Portable wall-clock guard: macOS has no `timeout` command.
(
    sleep "$WALL_CLOCK_GUARD_SECONDS"
    echo "WATCHDOG: run-profile.sh exceeded ${WALL_CLOCK_GUARD_SECONDS}s wall-clock guard, aborting." >&2
    kill -TERM "$$" 2>/dev/null || true
) &
WATCHDOG_PID=$!

echo "=== Verify parameters against task-1's committed baseline before doing anything else ==="
if [ ! -f "$TASK1_OUTPUT_DIR/load-test.txt" ]; then
    echo "FATAL: task-1's committed output/load-test.txt not found at $TASK1_OUTPUT_DIR - cannot verify like-for-like parameters." >&2
    exit 1
fi
TASK1_COMMAND_LINE="$(grep '^Command:' "$TASK1_OUTPUT_DIR/load-test.txt")"
TASK1_CONCURRENCY="$(echo "$TASK1_COMMAND_LINE" | grep -oE '\-c [0-9]+' | awk '{print $2}')"
TASK1_DURATION="$(echo "$TASK1_COMMAND_LINE" | grep -oE '\-d [0-9]+s' | awk '{print $2}')"
TASK1_TOOL_VERSION="$(grep '^bombardier version' "$TASK1_OUTPUT_DIR/environment.txt" || true)"

if [ "$TASK1_CONCURRENCY" != "$EXPECTED_CONCURRENCY" ]; then
    echo "FATAL: task-1's recorded concurrency ($TASK1_CONCURRENCY) does not match this script's EXPECTED_CONCURRENCY ($EXPECTED_CONCURRENCY)." >&2
    exit 1
fi
if [ "$TASK1_DURATION" != "$EXPECTED_DURATION" ]; then
    echo "FATAL: task-1's recorded duration ($TASK1_DURATION) does not match this script's EXPECTED_DURATION ($EXPECTED_DURATION)." >&2
    exit 1
fi
if [[ "$TASK1_TOOL_VERSION" != ${EXPECTED_TOOL_VERSION_PREFIX}* ]]; then
    echo "FATAL: task-1's recorded tool version ($TASK1_TOOL_VERSION) does not start with '$EXPECTED_TOOL_VERSION_PREFIX'." >&2
    exit 1
fi
echo "Confirmed: concurrency=$TASK1_CONCURRENCY, duration=$TASK1_DURATION, tool='$TASK1_TOOL_VERSION' all match task-1."

CURRENT_BOMBARDIER_VERSION="$(bombardier --version 2>&1)"
if [[ "$CURRENT_BOMBARDIER_VERSION" != "$TASK1_TOOL_VERSION" ]]; then
    echo "FATAL: the bombardier installed now ('$CURRENT_BOMBARDIER_VERSION') does not match task-1's recorded version ('$TASK1_TOOL_VERSION')." >&2
    exit 1
fi
echo "Confirmed: locally installed bombardier matches task-1's recorded version exactly."

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "=== Build ==="
dotnet build "$TASK_ROOT/Task2.slnx" --nologo -v minimal

echo "=== Pick a free port ==="
PORT=5187
while lsof -iTCP:"$PORT" -sTCP:LISTEN -P -n >/dev/null 2>&1; do
    PORT=$((PORT + 1))
done
BASE_URL="http://127.0.0.1:$PORT"
echo "Using port $PORT"

echo "=== Start API ==="
# Run from FastApi's own directory so ASP.NET Core's content root resolves there and
# appsettings.json (with the Microsoft.AspNetCore log-level override) is actually found -
# `dotnet <dll>` does NOT default the content root to the dll's own directory, only to the
# current working directory. Without this, default per-request Information logging would
# both bloat server.log and add real overhead that skews the high-throughput projection
# endpoint's measured latency.
(
    cd "$FASTAPI_PROJECT_DIR" && \
    ASPNETCORE_URLS="$BASE_URL" \
    FASTAPI_DB_PATH="$DB_PATH" \
    exec dotnet "$FASTAPI_DLL"
) > "$OUTPUT_DIR/server.log" 2>&1 &
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

run_variant_load_test() {
    local variant_name="$1"
    local endpoint_path="$2"

    echo "=== Warmup pass for '$variant_name' (discarded) ==="
    bombardier -c "$WARMUP_CONCURRENCY" -d "$WARMUP_DURATION" "$BASE_URL$endpoint_path" \
        > /dev/null 2>&1 || true

    echo "=== Real load test for '$variant_name' (captured) ==="
    local out_file="$OUTPUT_DIR/load-test-$variant_name.txt"
    {
        echo "Command: bombardier -c $EXPECTED_CONCURRENCY -d $EXPECTED_DURATION -l $BASE_URL$endpoint_path"
        echo ""
    } > "$out_file"
    bombardier -c "$EXPECTED_CONCURRENCY" -d "$EXPECTED_DURATION" -l "$BASE_URL$endpoint_path" \
        | tee -a "$out_file"
}

# PRIMARY fixed endpoint - projection - at the identical relative path task-1 used.
run_variant_load_test "projection" "/api/authors/quote-summary"

# SECOND fixed variant - Include with split queries.
run_variant_load_test "split" "/authors/quote-summary/split"

echo "=== Single-request SQL log, query plan, schema dump - projection ==="
dotnet "$FASTAPI_DLL" diagnostics projection "$DB_PATH" "$OUTPUT_DIR"

echo "=== Single-request SQL log, query plan, schema dump - split query ==="
dotnet "$FASTAPI_DLL" diagnostics split "$DB_PATH" "$OUTPUT_DIR"

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
         "shape and the relative before/after change are the finding."
} > "$OUTPUT_DIR/environment.txt"

echo "=== Done. Artefacts written to $OUTPUT_DIR ==="
