# KQL queries

**Status: pending** — no App Insights resource exists yet to run these against, so neither has produced a real result. Both queries are correct and ready to run the moment a resource exists; no result rows are invented below.

## The actual exercise: slowest 10 requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, duration, resultCode, operation_Id
```

**Why `requests`, not `traces`.** The task body's own example (below) queries `traces` — but `traces` holds free-text/structured *log* lines (what `ILogger`/Serilog calls emit), which have no inherent request-duration column. Every HTTP request handled by the app gets its own row in the separate `requests` table instead, and that row *does* carry a `duration` column (milliseconds) — because that table is populated by the AspNetCore instrumentation's request span, not by application log calls. "Slowest requests" is a question about `requests.duration`; it isn't answerable from `traces` at all without joining back to the request table by `operation_Id`.

**What each line does:**
- `requests` — start from the request-telemetry table (one row per HTTP request span).
- `| where timestamp > ago(1h)` — restrict to the last hour.
- `| top 10 by duration desc` — sort by duration, highest first, keep only 10. `top N by X` is the idiomatic single-operator way to do this in KQL (equivalent to `order by X desc | take N`, but expressed as one step).
- `| project timestamp, name, duration, resultCode, operation_Id` — pick which columns to show: when it ran, the request's route/name, how long it took, its status code, and the operation ID (useful for pivoting to the full trace for that request in the portal).

## The task body's example query

```kql
traces
| where timestamp > ago(15m)
| where customDimensions.UserId == "abc"
| order by timestamp asc
```

**What it does differently.** This queries `traces` (structured log lines, not request spans) from the last 15 minutes, filtered to a *specific* value of a custom property named `UserId`. This is exactly how a structured log property set via a message template — `logger.LogInformation("Login succeeded for user {UserId}", caller.UserId)` — shows up once exported to App Insights: as a key inside the dynamic `customDimensions` bag on that log row, queryable directly (`customDimensions.UserId`). It demonstrates *structured log property querying*, not request-latency analysis — a genuinely different use case from the exercise's actual ask, which is why the task's own wording flags it as worth explaining rather than reusing directly.
