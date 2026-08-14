## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-5/task-5/day-5/task-5

## Notes for mentor
Corrected query (`kql/request-latency.kql`, saved in the portal as function `RequestLatencyByEndpoint`):
```kql
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```
Real result: `GET /authors/fast` -- 8 requests, p50 3.5939ms, p99 329.2376ms; `GET /health` -- 6 requests, p50 0.4233ms, p99 96.1819ms; `GET /authors/slow` -- 8 requests, p50 3.3971ms, p99 4.116ms. Manual curl calls made: 5 to `/health`, 8 to `/authors/fast`, 8 to `/authors/slow` -- the fast/slow counts match exactly; `/health` shows one extra, most likely a Container Apps platform health probe. My observation: `/authors/fast`'s p99 (329ms) versus its p50 (3.6ms) surprised me -- nearly 100x worse for the unluckiest request on the endpoint that's supposed to be the fast one. This telemetry came from the app's own OpenTelemetry pipeline, not the Container Apps platform -- confirmed in Step 2 that neither path was active before this task, then fixed by adding `UseAzureMonitor()` (day-5/task-5's own copy of the app; Task 4 was not modified) and redeploying the existing Container App to a new revision on the existing registry/environment, creating no new Azure resource. The connection string was never printed or committed anywhere -- only its environment-variable name was ever queried. The screenshot was reviewed and re-cropped by the user to remove their account email before being committed. Commit hash: this same commit (a commit can't contain its own hash inside its own tree; see `git log -1` or the GitHub commit view).

## What did you learn this session?
`/authors/fast`'s real numbers showed a median of 3.6ms but a p99 of 329ms -- proof that an endpoint can look perfectly healthy on average while a real fraction of its requests have a far worse experience.

## What would break this?
The app's OpenTelemetry pipeline had no Azure Monitor exporter at all going into this task, so it was quietly sending traces to a local Jaeger address that doesn't exist in the cloud -- telemetry can look fine to a developer running locally while never reaching Azure at all once deployed.
