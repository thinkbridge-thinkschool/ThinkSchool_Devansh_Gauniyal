# Day 22 Task 1 — resilience evidence summary

All output in this directory is REAL captured log output from actually running the
app and driving it via curl — none of it is hand-written or simulated. It was
assembled from two real runs rather than one unbroken one; the honest account of why
is below, not glossed over.

## What happened

`scripts/capture-resilience-evidence.sh` drives five scenarios against a fresh
instance of the app on **production resilience tuning** (no `ResilienceTuning:*`
overrides — the real 5s sampling / 4 minimum throughput / 10s break / 2s timeout /
3-attempt retry values). The first full run (`full-server.txt`, started
`2026-09-04T12:12:04Z`, port 5411) successfully completed:

1. HTTP breaker lifecycle: closed → open → half-open → closed
2. Redis breaker lifecycle: closed → open → half-open → closed, plus the cached
   endpoint verified serving correct data from the database while the Redis breaker
   was open
3. Timeout (Slow mode, one call)
4. Bulkhead (Slow mode, 20 concurrent calls)

— and then hung during/after scenario 4's `wait` for 20 backgrounded curl processes:
the underlying `dotnet` process was still alive but stopped producing any further log
output, and the script's own 300-second watchdog did not reliably recover it. The
tail of `full-server.txt` shows a burst of `HttpConnectionPool` /
`HttpIOException: The response ended prematurely` errors around the time it stopped,
consistent with one of those 20 background curl processes blocking on a connection
that never completed — but the exact root cause was not fully isolated. The script
now sets `--max-time 20` on every curl call to bound that specific failure mode (see
the script's own header comment); this was not re-verified end-to-end because of the
time a full run costs (multiple real 10+ second waits), so treat that specific fix as
reasoned-through and applied, not re-proven by a clean full run.

Since a full clean run could not be reliably reproduced in time, the timeout and
bulkhead evidence was captured separately, directly, in isolation
(`clean-timeout-bulkhead-capture.txt`, port 5412, a completely fresh app instance
with the breaker starting Closed) rather than relying on the hung run's tail end. Both captures are
genuine command output; nothing in either was edited by hand beyond `grep`-filtering
into the focused files below.

## Files

- **`full-server.txt`** — raw Serilog console output from the first run, scenarios 1
  through 4 (before the hang). This is where `breaker-lifecycle.txt` and most of
  `retry-backoff.txt` were filtered from.
- **`clean-timeout-bulkhead-capture.txt`** — raw output from the separate, clean,
  direct capture (fresh app instance, breaker starts Closed). Source for `timeout.txt`
  and `bulkhead-rejection.txt`.
- **`breaker-lifecycle.txt`** — every `Circuit breaker OPENED` / `HALF-OPEN` /
  `CLOSED` line from `full-server.txt`, filtered with
  `grep -E "Circuit breaker (OPENED|CLOSED)|HALF-OPEN"`. Shows the HTTP breaker's full
  cycle (`12:12:08` open → `12:12:20` half-open → `12:12:20` closed, a real 12-second
  span against the configured 10s `BreakDuration`), the Redis breaker's full cycle
  (`12:12:20` → `12:12:33`, 13 seconds), and a third `OPENED` for the HTTP dependency
  at `12:12:47` — the bulkhead scenario's 20 concurrent Slow calls generated enough
  real timeouts to trip the breaker again mid-burst. That third entry is discussed
  below, not hidden.
- **`retry-backoff.txt`** — every real retry attempt and timeout event from
  `full-server.txt`. Contains two full retry sequences with real, jittered,
  non-strictly-increasing delays (e.g. one sequence: 0.874s → 1.457s → 0.840s; another:
  0.935s → 0.577s → 2.788s) — see README.md for why jitter can make one delay shorter
  than the previous one, which is expected, not a bug.
- **`timeout.txt`** — from the clean capture: one real `TimeoutRejectedException`
  ("The operation didn't complete within the allowed timeout of '00:00:02'") firing
  against a Slow-mode call, with the breaker confirmed still `Closed` afterward (one
  timeout alone doesn't meet `MinimumThroughput`).
- **`bulkhead-rejection.txt`** — from the clean capture: 8 real
  `Bulkhead REJECTED a call to external-service` lines, one per rejected call.
- **`bulkhead-responses.txt`** — the raw JSON outcome of each of the 20 concurrent
  calls fired at the bulkhead scenario in the clean capture. Real tally: **8
  bulkhead-rejected, 12 short-circuited, 0 succeeded** — every one of the 20 calls was
  Slow-mode, and by the time the 12 bulkhead-admitted calls had accumulated enough
  real timeouts, the breaker itself opened mid-burst and short-circuited the rest.
  This is the honest, composed-pipeline result, not a clean "N ok / M rejected" split
  — that clean, mechanism-isolated split (3 ok / 3 rejected against a breaker-free
  pipeline) is what
  `QuotesApi.Tests.ResilienceTests.Bulkhead_RejectsWorkBeyondItsConcurrencyLimit`
  proves instead, deliberately in isolation from the breaker. See README.md's
  "honest limits" section.

## Exact commands and parameters used

```bash
cd day-22/task-1
./scripts/capture-resilience-evidence.sh   # full run: port 5411, production tuning
```

Clean supplementary capture (direct, not scripted — see above for why):

```bash
cd day-22/task-1/QuotesApi/bin/Debug/net10.0
ASPNETCORE_URLS=http://localhost:5412 ASPNETCORE_ENVIRONMENT=Development \
  PERFORMANCE_DB_PATH=../../../output/evidence-performance.db \
  InternalJwt__Issuer=evidence.local InternalJwt__Audience=evidence.local.clients \
  InternalJwt__SigningKeyBase64=<generated> InternalCaller__UserId=evidence-user \
  InternalCaller__Email=evidence@example.test \
  InternalCaller__PasswordSaltBase64=<generated> InternalCaller__PasswordHashBase64=<generated> \
  dotnet QuotesApi.dll

# Timeout:
curl -X POST 'http://localhost:5412/api/faults/external-service?mode=slow'
curl 'http://localhost:5412/api/resilience/external/call'

# Bulkhead:
curl -X POST 'http://localhost:5412/api/faults/external-service?mode=slow'
for i in $(seq 1 20); do curl 'http://localhost:5412/api/resilience/external/call' & done; wait
```
