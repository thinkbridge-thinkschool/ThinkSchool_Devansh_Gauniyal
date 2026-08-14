# Day 5 Task 5 — Real Results

Real query result, read directly from the Application Insights Logs blade (`appi-hvnke3dqrbwrq`), reported by the user after running the saved function `RequestLatencyByEndpoint`. Screenshot: `screenshots/kql-result.png`.

## Query result

| name | count_ | p50 (ms) | p99 (ms) |
|---|---|---|---|
| GET /authors/fast | 8 | 3.5939 | 329.2376 |
| GET /health | 6 | 0.4233 | 96.1819 |
| GET /authors/slow | 8 | 3.3971 | 4.116 |

## Calls made in Step 3, for cross-checking the count() column

| Endpoint | Manual curl calls made |
|---|---|
| /health | 5 |
| /authors/fast | 8 |
| /authors/slow | 8 |

`/authors/fast` and `/authors/slow` match exactly (8 and 8). `/health` shows **6** in the query result against **5** manual calls -- one extra request the app itself received and reported, not an error in the query or the count.

## My observation

`/authors/fast`'s p99 is what actually surprised me: the median (p50) is only 3.6ms, but the p99 jumps all the way to 329ms -- almost 100x worse for the unluckiest request, on the endpoint that's supposed to be the fast one with nothing deliberately slow in it. An average alone would never have shown this; only looking at the tail did.

## Where the telemetry actually came from

This telemetry came from **the app's own OpenTelemetry pipeline**, not from the Container Apps platform. Step 2 of this task established that neither path was active on the code as deployed at the start of this task: the app only had an OTLP exporter targeting a local Jaeger address, and Container Apps' own environment-level auto-telemetry (`appInsightsConfiguration`/`openTelemetryConfiguration`) was confirmed unset. The fix applied in this task added `UseAzureMonitor()` to the app's existing `AddOpenTelemetry()` registration, reading the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable that was already present but previously unused. The `requests` table populating with real per-endpoint data after that change, and not before, is itself the evidence: this is app-level OpenTelemetry instrumentation reaching Azure Monitor, not a platform-level side channel.

The extra `/health` request (6 vs. 5 manual calls) is still consistent with this: it's most likely Azure Container Apps' own health/readiness probe hitting the same `/health` endpoint on its own schedule -- but because it's a real HTTP request the app actually received and handled, the app's `AddAspNetCoreInstrumentation()` picked it up like any other request. That's a probe *causing* extra app-level telemetry, not a separate platform telemetry source bypassing the app.
