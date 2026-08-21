#!/bin/bash
# Orchestrates the full Day 11 Task 1 measurement end to end against the REAL Week-1 API
# (day-3/task-3/QuotesApi): builds it, starts it on a free local port, warms it up, runs
# the real load test with bombardier, captures a single request's SQL log, the EXPLAIN
# QUERY PLAN for the per-author query, and a dump of the Quotes table's indexes. Always
# stops the API afterwards, even on failure.
#
# The real QuotesApi validates several configuration sections eagerly at startup
# (InternalJwt signing key, InternalCaller credentials) that are normally supplied via
# .NET user-secrets and are NOT touched by this script. Instead, this script generates its
# OWN fresh, synthetic, throwaway bootstrap values every run, passed only as environment
# variables to the child process for its lifetime - never written to disk, never
# committed, never derived from or overlapping with any real secret.
#
# Usage: scripts/run-profile.sh
# Re-runnable from a clean state with no manual steps.

set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "$TASK_ROOT/../.." && pwd)"
OUTPUT_DIR="$TASK_ROOT/output"
DB_PATH="$OUTPUT_DIR/performance.db"
QUOTESAPI_PROJECT_DIR="$REPO_ROOT/day-3/task-3/QuotesApi"
QUOTESAPI_DLL="$QUOTESAPI_PROJECT_DIR/bin/Debug/net10.0/QuotesApi.dll"

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

echo "=== Generate fresh synthetic bootstrap secrets (never persisted) ==="
INTERNAL_JWT_SIGNING_KEY="$(openssl rand -base64 32)"
INTERNAL_CALLER_SALT="$(openssl rand -base64 16)"
INTERNAL_CALLER_HASH="$(openssl rand -base64 32)"

echo "=== Start API (real day-3/task-3/QuotesApi) ==="
# Run from QuotesApi's own directory so ASP.NET Core's content root resolves there and
# appsettings.json (with the Entra TenantId/Audience) is actually found - `dotnet <dll>`
# does NOT default the content root to the dll's own directory, only to the current
# working directory.
(
    cd "$QUOTESAPI_PROJECT_DIR" && \
    ASPNETCORE_ENVIRONMENT=Testing \
    ASPNETCORE_URLS="$BASE_URL" \
    PERFORMANCE_DB_PATH="$DB_PATH" \
    InternalJwt__SigningKeyBase64="$INTERNAL_JWT_SIGNING_KEY" \
    InternalCaller__UserId="perf-test-user" \
    InternalCaller__Email="perf-test@example.invalid" \
    InternalCaller__PasswordSaltBase64="$INTERNAL_CALLER_SALT" \
    InternalCaller__PasswordHashBase64="$INTERNAL_CALLER_HASH" \
    exec dotnet "$QUOTESAPI_DLL"
) > "$OUTPUT_DIR/server.log" 2>&1 &
SERVER_PID=$!

echo "Polling $BASE_URL/ until it responds (pid $SERVER_PID)..."
DEADLINE=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
until curl -sf "$BASE_URL/" >/dev/null 2>&1; do
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
bombardier -c "$WARMUP_CONCURRENCY" -d "$WARMUP_DURATION" "$BASE_URL/api/authors/quote-summary" \
    > /dev/null 2>&1 || true

echo "=== Real load test (captured) ==="
BOMBARDIER_CMD="bombardier -c $LOAD_CONCURRENCY -d $LOAD_DURATION -l $BASE_URL/api/authors/quote-summary"
{
    echo "Command: $BOMBARDIER_CMD"
    echo ""
} > "$OUTPUT_DIR/load-test.txt"
bombardier -c "$LOAD_CONCURRENCY" -d "$LOAD_DURATION" -l "$BASE_URL/api/authors/quote-summary" \
    | tee -a "$OUTPUT_DIR/load-test.txt"

echo "=== Single-request SQL log, query plan, schema dump ==="
# Standalone diagnostics pass - builds no web host, so none of the JWT/Entra/caller
# config above is needed here.
dotnet "$QUOTESAPI_DLL" performance-diagnostics "$DB_PATH" "$OUTPUT_DIR"

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
