# Day 21 Task 1 — measurement summary

Generated 2026-09-03 06:25:07Z by `scripts/parse-measurement.cs` from the raw
bombardier reports and DB-query-counter snapshots in this directory. Percentiles are
read directly out of bombardier's own report using the parsing pattern copied from
`day-11/task-1/QuotesApi.Performance.Tests/LatencyPercentileParser.cs` — not
estimated or rounded by hand.

**Parameters:** concurrency=20, duration=10s (10s)

| Path | Total requests | Real DB queries | Factory runs | DB queries/sec | Latency p50/p90/p99 |
|---|---:|---:|---:|---:|---|
| Uncached | 977 | 49827 | 977.0 | 4982.70 | 50%=172.54ms / 90%=246.14ms / 99%=777.14ms |
| Cached | 656316 | 51 | 1.0 | 5.10 | 50%=0.25ms / 90%=0.44ms / 99%=1.25ms |

**Cache hit rate (cached path, key starts cold - the first concurrent wave races and coalesces, then every later request is a real hit):**
100.0% (656315 of 656316 requests served without a DB round trip)

**DB load drop:** 4982.70 → 5.10 DB queries/sec
(99.9% reduction)

These are local, single-machine numbers against SQLite with an artificial DB delay
(`Measurement:ArtificialDbDelayMs`) — see README.md for what that means for how far
they generalise.