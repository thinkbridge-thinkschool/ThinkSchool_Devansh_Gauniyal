#!/bin/bash
# Drives the Day 22 Task 1 resilience pipeline through real scripted scenarios and
# captures REAL, timestamped log output into output/ - not a hand-written narrative of
# what would happen. Same discipline as day-21/task-1/scripts/run-measurement.sh:
# builds the app, starts it on a free local port, drives it via curl against the
# fault-injection switches, and always stops it afterward, even on failure.
#
# Uses PRODUCTION resilience tuning (no ResilienceTuning:* overrides) so the captured
# evidence reflects the values actually shipped, not a sped-up demo configuration:
# HTTP breaker 5s sampling / 4 minimum throughput / 10s break; Redis breaker the same;
# HTTP timeout 2s; retry 3 attempts exponential+jitter. Total run time is a few
# minutes (each break duration is a real 10s wait), not sped up.
#
# Usage: scripts/capture-resilience-evidence.sh
# Re-runnable from a clean state with no manual steps.
#
# Every curl call carries --max-time 20 - added after a real hang: a full run
# eventually stopped producing output entirely (the underlying dotnet process was
# still alive, just idle) and the WALL_CLOCK_GUARD_SECONDS watchdog below did not
# reliably recover it - the captured log's tail shows a burst of
# HttpConnectionPool/HttpIOException "response ended prematurely" errors, consistent
# with one or more of the 20 backgrounded curl processes in the bulkhead scenario
# blocking on a connection with no read ever completing, which left the script's
# `wait` for those background jobs blocked indefinitely. --max-time bounds every curl
# call so that specific failure mode can't hang the script again; the exact root cause
# was not fully isolated. See README.md's verification log for the full account,
# including that the evidence in output/ was assembled from more than one real run
# after this was found, not fabricated to paper over it.

set -uo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="$TASK_ROOT/output"
QUOTESAPI_PROJECT_DIR="$TASK_ROOT/QuotesApi"
QUOTESAPI_DLL="$QUOTESAPI_PROJECT_DIR/bin/Debug/net10.0/QuotesApi.dll"
PORT=5411
BASE_URL="http://localhost:$PORT"
DB_PATH="$OUTPUT_DIR/evidence-performance.db"
HEALTH_TIMEOUT_SECONDS=30
WALL_CLOCK_GUARD_SECONDS=300

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

(
    sleep "$WALL_CLOCK_GUARD_SECONDS"
    echo "WATCHDOG: capture-resilience-evidence.sh exceeded ${WALL_CLOCK_GUARD_SECONDS}s, aborting." >&2
    kill -TERM "$$" 2>/dev/null || true
) &
WATCHDOG_PID=$!

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
rm -f "$DB_PATH" "$DB_PATH-shm" "$DB_PATH-wal"

echo "Building QuotesApi..."
dotnet build "$QUOTESAPI_PROJECT_DIR/QuotesApi.csproj" -c Debug >/dev/null

echo "Starting QuotesApi on $BASE_URL (production resilience tuning, no overrides)..."
(
    cd "$(dirname "$QUOTESAPI_DLL")"
    PERFORMANCE_DB_PATH="$DB_PATH" \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="$BASE_URL" \
    InternalJwt__Issuer="evidence.local" \
    InternalJwt__Audience="evidence.local.clients" \
    InternalJwt__SigningKeyBase64="$(openssl rand -base64 32)" \
    InternalCaller__UserId="evidence-user" \
    InternalCaller__Email="evidence@example.test" \
    InternalCaller__PasswordSaltBase64="$(openssl rand -base64 16)" \
    InternalCaller__PasswordHashBase64="$(openssl rand -base64 32)" \
    exec dotnet "$QUOTESAPI_DLL"
) > "$OUTPUT_DIR/full-server.txt" 2>&1 &
SERVER_PID=$!

echo "Waiting for health..."
elapsed=0
until curl -sf --max-time 20 "$BASE_URL/" >/dev/null 2>&1; do
    sleep 1
    elapsed=$((elapsed + 1))
    if [ "$elapsed" -ge "$HEALTH_TIMEOUT_SECONDS" ]; then
        echo "API did not become healthy within ${HEALTH_TIMEOUT_SECONDS}s." >&2
        exit 1
    fi
done
echo "API is up (pid $SERVER_PID)."

mark() { echo "" >> "$OUTPUT_DIR/full-server.txt"; echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) SCENARIO: $1 ===" >> "$OUTPUT_DIR/full-server.txt"; echo "--- $1 ---"; }

# ===================================================================
# Scenario 1: HTTP breaker lifecycle - closed -> open -> half-open -> closed
# ===================================================================
mark "HTTP breaker: reset to healthy baseline"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=healthy" > /dev/null
curl -sf --max-time 20 "$BASE_URL/api/resilience/breakers"; echo ""

mark "HTTP breaker: inject Failing, drive sustained failure"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=failing" > /dev/null
for i in $(seq 1 6); do
    curl -s --max-time 20 "$BASE_URL/api/resilience/external/call" > /dev/null
done
echo "Breaker state after sustained failure:"
curl -sf --max-time 20 "$BASE_URL/api/resilience/breakers"; echo ""

mark "HTTP breaker: waiting out the real BreakDuration (10s + buffer)"
sleep 12

mark "HTTP breaker: switch back to Healthy, fire the half-open probe"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=healthy" > /dev/null
curl -s --max-time 20 "$BASE_URL/api/resilience/external/call"; echo ""
echo "Breaker state after successful probe:"
curl -sf --max-time 20 "$BASE_URL/api/resilience/breakers"; echo ""

# ===================================================================
# Scenario 2: Redis breaker lifecycle - closed -> open -> half-open -> closed,
# plus graceful degradation of the real cached endpoint while open.
# ===================================================================
mark "Redis breaker: reset to healthy baseline"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/redis?mode=healthy" > /dev/null

mark "Redis breaker: inject Failing, drive sustained failure"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/redis?mode=failing" > /dev/null
for i in $(seq 1 6); do
    curl -s --max-time 20 "$BASE_URL/api/resilience/redis/call" > /dev/null
done
echo "Breaker state after sustained failure:"
curl -sf --max-time 20 "$BASE_URL/api/resilience/breakers"; echo ""

mark "Redis breaker open: cached endpoint must still serve correct data from the database"
curl -sf --max-time 20 -X POST "$BASE_URL/api/measurement/reset" > /dev/null
curl -s --max-time 20 "$BASE_URL/api/authors/quote-summary/cached?key=evidence-degrade" -o /dev/null -w "HTTP status: %{http_code}\n"
echo "DB query count (51 expected - served from the database, not Redis):"
curl -sf --max-time 20 "$BASE_URL/api/measurement/db-query-count"; echo ""

mark "Redis breaker: waiting out the real BreakDuration (10s + buffer)"
sleep 12

mark "Redis breaker: switch back to Healthy, fire the half-open probe"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/redis?mode=healthy" > /dev/null
curl -s --max-time 20 "$BASE_URL/api/resilience/redis/call"; echo ""
echo "Breaker state after successful probe:"
curl -sf --max-time 20 "$BASE_URL/api/resilience/breakers"; echo ""

# ===================================================================
# Scenario 3: timeout (Slow mode). Deliberately BEFORE the retry scenario: retry's
# single logical call against sustained Failing produces enough failed attempts
# (MaxRetryAttempts=3 => 4 attempts) to trip MinimumThroughput on its own and reopen
# the breaker - confirmed the hard way (an earlier run of this exact script put retry
# third and it reopened the breaker, which then silently short-circuited both the
# timeout and bulkhead scenarios that followed instead of demonstrating either. See
# README.md's verification log). Timeout and bulkhead run first instead, while the
# breaker is still closed from the Redis scenario above (which never touches
# external-service).
# ===================================================================
mark "Timeout: inject Slow on the HTTP dependency, fire one call"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=slow" > /dev/null
curl -s --max-time 20 "$BASE_URL/api/resilience/external/call"; echo ""
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=healthy" > /dev/null

# ===================================================================
# Scenario 4: bulkhead rejection (concurrent slow calls beyond the permit+queue limit)
# ===================================================================
mark "Bulkhead: inject Slow, fire 20 concurrent calls (permit=8, queue=4 -> 12 admitted, 8 rejected)"
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=slow" > /dev/null
for i in $(seq 1 20); do
    curl -s --max-time 20 "$BASE_URL/api/resilience/external/call" >> "$OUTPUT_DIR/bulkhead-responses.txt" &
done
wait
echo "" >> "$OUTPUT_DIR/bulkhead-responses.txt"
echo "Bulkhead outcome tally:"
grep -o '"outcome":"[a-z-]*"' "$OUTPUT_DIR/bulkhead-responses.txt" | sort | uniq -c
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=healthy" > /dev/null

# ===================================================================
# Scenario 5: retry with real backoff delays (idempotent HTTP dependency only). Runs
# LAST on purpose - see the note above scenario 3.
# ===================================================================
mark "Retry: reset, inject Failing, fire ONE call and capture the real backoff delays"
sleep 1
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=failing" > /dev/null
curl -s --max-time 20 "$BASE_URL/api/resilience/external/call"; echo ""
curl -sf --max-time 20 -X POST "$BASE_URL/api/faults/external-service?mode=healthy" > /dev/null

echo ""
echo "Stopping API..."
kill "$SERVER_PID" 2>/dev/null || true
wait "$SERVER_PID" 2>/dev/null || true
SERVER_PID=""
sleep 1

# ===================================================================
# Extract focused evidence files from the real captured server log
# ===================================================================
echo "Extracting focused evidence from full-server.txt..."
grep -E "Circuit breaker|HALF-OPEN|OPENED|CLOSED" "$OUTPUT_DIR/full-server.txt" > "$OUTPUT_DIR/breaker-lifecycle.txt" || true
grep -E "Retry attempt|RetryDelay|Execution attempt.*Retry" "$OUTPUT_DIR/full-server.txt" > "$OUTPUT_DIR/retry-backoff.txt" || true
grep -E "Timeout fired|didn't complete within" "$OUTPUT_DIR/full-server.txt" > "$OUTPUT_DIR/timeout.txt" || true
grep -E "Bulkhead REJECTED" "$OUTPUT_DIR/full-server.txt" > "$OUTPUT_DIR/bulkhead-rejection.txt" || true

echo "Done. Raw and extracted evidence is in $OUTPUT_DIR:"
ls -la "$OUTPUT_DIR"
