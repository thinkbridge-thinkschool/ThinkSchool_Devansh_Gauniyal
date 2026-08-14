# Day 5 Task 5 — Notes

## What Application Insights is, and how it relates to Log Analytics

Application Insights is Azure's application-performance-monitoring product: it collects requests, dependencies, exceptions, and traces from an app and lets you query them. Under the hood, an Application Insights resource stores its data inside a Log Analytics workspace (in this task, `log-hvnke3dqrbwrq`, confirmed via `az resource list`) -- Application Insights is really a friendly, app-focused layer on top of the same Log Analytics storage and query engine everything else in Azure Monitor uses. That's why opening "Logs" from the Application Insights resource itself shows familiar table names like `requests` (lowercase, classic Application Insights schema), while the same data queried from the raw Log Analytics workspace shows up as `AppRequests` (PascalCase, the underlying table).

## What the requests table contains, and where its rows come from

Each row is one incoming HTTP request the instrumented app actually handled -- confirmed in this task to come specifically from the app's own OpenTelemetry pipeline once `UseAzureMonitor()` was added (see `results.md` for the full reasoning). Real columns used here: `name` (which endpoint, e.g. "GET /health" -- verified against the official `AppRequests` schema as the column meant for per-endpoint identification, distinct from `operation_Name` which correlates a whole multi-step transaction) and `duration` (how long the app took to handle it, in milliseconds).

## What percentile() does, and why p50 + p99 together tell a story an average can't

`percentile(duration, N)` finds the value below which N% of requests fall. `p50` (the median) is what a typical request experiences. `p99` is what the unluckiest 1% experiences. The real numbers from this task show exactly why both matter: `/authors/fast`'s p50 was 3.6ms -- genuinely fast -- but its p99 was 329ms, nearly 100x worse. An average across all 8 requests would land somewhere in between and hide that spike entirely, making the endpoint look consistently fine when a real fraction of its users had a much worse experience. p99 can't be smoothed away like that.

## What saving a query as a function gives you

A saved function (`RequestLatencyByEndpoint`, created via **Save > Save as function** in the Logs blade, confirmed against the official steps at https://learn.microsoft.com/en-us/azure/azure-monitor/logs/functions) turns this query into something callable by name from any other query in the same workspace, instead of needing to copy-paste the whole KQL text every time. It's the difference between a saved snippet and a reusable building block.

## How this closes the loop from Task 1

Day 5 Task 1 (`day-5/task-1/kql/slow-endpoints.kql`) wrote a KQL query for finding slow endpoints in Application Insights, but explicitly marked it "written and reviewed, not verified against live data" -- that project only ever sent telemetry to a local Jaeger instance, with no Application Insights resource in the picture at all. This task is the first time in the whole Day 5 sequence a KQL query has actually been run against live data with real, confirmed numbers coming back, closing that gap.

## What the connection string is, why it's a secret, and why it must never appear in the repo

`APPLICATIONINSIGHTS_CONNECTION_STRING` contains an instrumentation key -- a value that lets anything holding it write telemetry into this specific Application Insights resource. Confirmed throughout this task: only the environment variable's *name* was ever queried or displayed (`az containerapp show ... --query "...env[].name"`), never its value. It is not included, redacted or otherwise, anywhere in this repository.

## Teardown situation, as it now stands

Unchanged from Task 4's final state: `rg-task4` is the only resource group left in the subscription (Task 3's `thinkschool-rg` was already deleted, with explicit approval, during Task 4 to free a subscription-wide Container Apps environment quota). This task added no new Azure resource -- it updated the existing Container App to a new container image/revision and read data from the Application Insights resource that already existed. See `submission.md` and the final report for the current teardown recommendation.
