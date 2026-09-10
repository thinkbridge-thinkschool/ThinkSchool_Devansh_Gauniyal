# Day 26 Task 1 — App Insights + KQL

## Notes for mentor

The live project is `capstone/` — `day-26/task-1/` is a frozen snapshot of it as
this day ended, copied after the work below, never edited directly.

The trace-demo worker (`host/Capstone.Worker`) exists solely to demonstrate
distributed tracing across a message broker — it is **not** the InvoiceApproved
integration event or supplier-notification async flow `DESIGN.md` names; those
remain unbuilt, exactly as they were through Day 25.

No plaintext secret was introduced: every new app setting on both Web Apps is
either a non-secret identifier/hostname or a Key Vault reference — read back
live and reproduced in the "Proof of zero plaintext secrets" section below.

Commit representing this day's state: `<filled in after commit>`.

Dev infrastructure is intentionally left running (26 resources per
`az stack sub show`: Day 25's 16 plus Log Analytics, App Insights, its
daily-cap sub-resource, an action group, an error-rate alert, and a second Web
App for the worker) — see `infra/README.md`'s cost section for the exact
teardown command. Real ingestion today: **2.06 MB** total
(`Usage | summarize sum(Quantity)`), against the 1 GB/day cap — 0.2% of it.

### The KQL, run for real

Real traffic: 153 requests generated in one batch (60x `GET /`, 40x
`POST /demo/submit-sample-invoice`, 30x `GET /demo/db-ping`, 15 deliberate
404s, 8x `POST /demo/trace-worker`) plus prior manual verification traffic and
a later, separate 13-request failure burst used to test the alert. Ingestion
wait: ~35 seconds from traffic completion to the first query returning data
(confirmed by polling `requests | count` until it matched, not a fixed guess).

**1. p50/p99 latency by endpoint** (`observability/queries/01-latency-p50-p99-by-endpoint.kql`)

| Endpoint | Requests | p50 (ms) | p99 (ms) |
|---|---|---|---|
| `GET /` | 65 | 0.28 | 605.38 |
| `POST /demo/submit-sample-invoice` | 40 | 0.31 | 160.37 |
| `GET /demo/db-ping` | 30 | 9.12 | 2439.92 |
| `ServiceBusProcessor.ProcessMessage` (worker) | 14 | 250.58 | 16052.74 |
| `capstone-worker.process-trace-demo-message` (worker) | 14 | 250.43 | 14917.84 |
| `POST /demo/trace-worker` | 10 | 988.53 | 7218.59 |
| 15x `GET /demo/this-route-does-not-exist-N` | 1 each | ~0.1–7.7 | ~0.1–7.7 |

Full result: `observability/results/01-latency-p50-p99-by-endpoint.json`.

**What it shows:** the in-memory endpoints (`/`, `/demo/submit-sample-invoice`)
are sub-millisecond at p50 with an occasional cold-start-driven p99 spike
(~600ms, ~160ms). `/demo/db-ping`'s p99 (2.4s) and the worker operations' p99
(15-16s) are real, explainable, not query artifacts: the dev SQL database is
Serverless with auto-pause, and several of these requests hit the database
while it was resuming from an idle pause (see `infra/VERIFICATION-LOG.md` §7).
Each 404 route got its own row because ASP.NET Core has no matched endpoint
template to normalize an unmatched path by — expected, not a bug.

**2. Dependency call breakdown** (`observability/queries/02-dependency-breakdown.kql`)

| Type | Operation | Target | Calls | Avg (ms) | p95 (ms) | Success % |
|---|---|---|---|---|---|---|
| HTTP | `GET /msi/token` | managed-identity endpoint | 2 | 2067.62 | 3133.28 | 100 |
| InProc \| Microsoft.AAD | `DefaultAzureCredential.GetToken` | — | 10 | 365.20 | 3363.88 | 100 |
| InProc \| Microsoft.AAD | `ManagedIdentityCredential.GetToken` | — | 1 | 1042.16 | 1042.16 | 100 |
| Other | `capstone-api.send-trace-demo-message` | — | 10 | 1618.07 | 7109.69 | 100 |
| SQL | `SQL: SELECT` | sqldb-capstone-dev | 30 | 14.19 | 30.00 | 100 |
| SQL | `SQL: INSERT dbo.TraceDemoEvents` | sqldb-capstone-dev | 11 | 68.24 | 202.11 | 81.8 |
| servicebus | `ServiceBusReceiver.Receive` | supplier-notifications/trace-demo-worker | 31 | 34745.46 | 60549.54 | 100 |
| servicebus | `ServiceBusSender.Send` | supplier-notifications | 10 | 1548.25 | 6739.34 | 100 |
| servicebus | `ServiceBusReceiver.Complete` | supplier-notifications/trace-demo-worker | 9 | 99.34 | 152.88 | 100 |
| servicebus | `ServiceBusReceiver.Abandon` | supplier-notifications/trace-demo-worker | 5 | 76.83 | 282.40 | 100 |

Full result: `observability/results/02-dependency-breakdown.json`.

**What it shows:** the only real outbound HTTP dependency this scaffold has
is the managed-identity token endpoint itself — there's no other outbound
HTTP call to instrument yet, which is itself an honest, correct result, not a
gap in the query. `SQL: INSERT`'s 81.8% success rate and the 5
`ServiceBusReceiver.Abandon` calls are the same real SQL-serverless-auto-pause
retries visible in query 1, now visible from the dependency side. Long
`ServiceBusReceiver.Receive` durations are the processor's long-poll
receive loop, not per-message latency.

**3. Error rate over time, by endpoint** (`observability/queries/03-error-rate-by-endpoint.kql`)

Full result: `observability/results/03-error-rate-by-endpoint.json` (29 rows,
5-minute buckets). Representative rows:

| Endpoint | Bucket | Total | Failed | Error % |
|---|---|---|---|---|
| `GET /` | 11:30 | 1 | 1 | 100 |
| `GET /` | 11:50 | 60 | 0 | 0 |
| `GET /demo/db-ping` | 11:50 | 30 | 0 | 0 |
| `POST /demo/submit-sample-invoice` | 11:50 | 40 | 0 | 0 |
| 15x `GET /demo/this-route-does-not-exist-N` | 11:50 | 1 each | 1 each | 100 |

**What it shows:** every deliberately-broken request reads as a clean 100%
failure bucket; every real endpoint stays at 0% across the whole session
except one incidental early single-request blip (1 request, 1 failure — a
manual curl against a URL that hadn't been deployed yet during earlier
debugging, correctly captured, not filtered out).

**4. Distributed trace, API → worker → DB** (`observability/queries/04-distributed-trace-api-worker-db.kql`)

Trace id: `d6fb2be59b61c555d076a18a861c234a`. Full result:
`observability/results/04-distributed-trace-api-worker-db.json` (14 items, one
trace id, in order):

| Time (UTC) | Item | Service | Name | Parent id |
|---|---|---|---|---|
| 11:50:15.835 | request | capstone-dev-api-f7nsoj | `POST /demo/trace-worker` | (root) |
| 11:50:15.847 | dependency | capstone-dev-api-f7nsoj | `capstone-api.send-trace-demo-message` | ad7c5c0c56589f03 |
| 11:50:15.858 | dependency | capstone-dev-api-f7nsoj | `ServiceBusSender.Send` | 8f9c0b24d27e46d7 |
| 11:50:15.994 | dependency | capstone-dev-api-f7nsoj | `DefaultAzureCredential.GetToken` | d481e2e1788da927 |
| 11:50:16.0xx | trace x4 | capstone-dev-api-f7nsoj | MSAL token-cache logs | dbb9db3cec48c270 |
| 11:50:16.832 | **request** | **capstone-worker** | `ServiceBusProcessor.ProcessMessage` | 8f9c0b24d27e46d7 |
| 11:50:16.833 | **request** | **capstone-worker** | `capstone-worker.process-trace-demo-message` | 8f9c0b24d27e46d7 |
| 11:50:16.833 | trace | capstone-worker | "received message ..., parent context found: True" | 5571a5b1562ee24b |
| 11:50:16.837 | dependency | capstone-worker | `SQL: INSERT dbo.TraceDemoEvents` | 5571a5b1562ee24b |
| 11:50:16.938 | dependency | capstone-worker | `ServiceBusReceiver.Complete` | d47e7b830d8fa116 |
| 11:50:17.093 | trace | capstone-worker | "completed message ... with trace id d6fb2be5..." | d47e7b830d8fa116 |

**What it shows:** one trace id, correctly parent-chained end to end across
TWO distinct `cloud_RoleName` values (proof this is genuinely two processes,
not one process logging twice), through the Service Bus hop, into a real SQL
write. Both the Azure SDK's own automatic `ServiceBusProcessor.ProcessMessage`
span and this project's manual `capstone-worker.process-trace-demo-message`
span independently agree on the same parent — see
`infra/VERIFICATION-LOG.md` §8.

**Evidence a distributed trace spans API → worker → DB**: the table above,
backed by the real, captured JSON result. I will attach the portal screenshot
myself.

### The alert definition

Declared in `infra/modules/alerting.bicep` as `Microsoft.Insights/scheduledQueryRules`
(`alert-capstone-dev-error-rate`, kind `LogAlert`):

- **Query:** `requests | summarize Total=count(), Failed=countif(success==false) | extend ErrorRatePercent=... | where Total >= 5 | project ErrorRatePercent`
- **Threshold:** `ErrorRatePercent > 30` (GreaterThan, timeAggregation Maximum)
- **Window:** 15 minutes, evaluated every 5 minutes
- **Severity:** 2 (Warning)
- **Action:** email, via action group `ag-capstone-dev-errors`
- **`skipQueryValidation: true`** — required because `requests` doesn't exist
  in a brand-new workspace at deploy time; see `VERIFICATION-LOG.md` §2.

**Verification attempt:** triggered a real, isolated failure burst (13 requests,
~10 deliberate 404s once the earlier bulk traffic aged out of the 15-minute
window) and confirmed via direct KQL that the condition genuinely held —
62.5%–77% error rate over 13-24 requests, both well past the 30%/5-request
bar — continuously for ~20 minutes (12:07-12:27 UTC), spanning at least three
of the rule's own 5-minute evaluation cycles. Polled
`Microsoft.AlertsManagement/alerts` the entire time (a fresh check every
~35-45s); zero fired instances appeared. The rule is confirmed `enabled: true`
via `az resource show`, and its query is confirmed correct against real,
current data — but I cannot confirm the alert actually fires without waiting
longer than this session allowed. Reporting that honestly rather than
claiming success I didn't observe: the condition and the wiring are both
real and verified; whether the rule itself fires on schedule is not yet
confirmed.

### Proof of zero plaintext secrets

`az webapp config appsettings list`, read back live after all Day 26 changes,
on both Web Apps — every value is a non-secret identifier/hostname or a Key
Vault reference, never a plaintext connection string or key:

| App setting (both apps) | Value shape |
|---|---|
| `AZURE_CLIENT_ID` | a GUID (identity client id) |
| `ConnectionStrings__CapstoneDb` | `Authentication=Active Directory Managed Identity`, no password |
| `ServiceBus__FullyQualifiedNamespace` | a hostname |
| `ServiceBus__DemoSubscriptionTopicName` / `DemoSubscriptionName` | plain strings |
| `OTEL_SAMPLING_RATIO` | `1.0` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `@Microsoft.KeyVault(SecretUri=...)` — confirmed `"status": "Resolved"` via `configreferences/appsettings` |
| `DemoConfig__SampleSetting` (API only, unchanged from Day 25) | `@Microsoft.KeyVault(SecretUri=...)` |

### Manual step — performed by Devansh, not automated

One SQL DDL statement, run in Portal Query Editor as the Entra admin (the app
identity has `db_datareader`/`db_datawriter` from Day 25, not DDL rights —
least privilege, so this wasn't automated):

```sql
CREATE TABLE dbo.TraceDemoEvents (
    Id INT IDENTITY PRIMARY KEY,
    TraceId NVARCHAR(64) NOT NULL,
    MessageId NVARCHAR(128) NOT NULL,
    ProcessedAtUtc DATETIME2 NOT NULL
);
```

## What did you learn this session?
A trace never survives a message queue on its own — I had to manually copy the trace id into the message on the way out and read it back on the way in myself, or the worker's half of the story would look completely disconnected from the API's.
I also learned a "working" database connection string can still fail the moment it's actually used for real, because the library needed a second, separate package for the exact login method I was already using, and nothing caught that until the live app tried it.

## What would break this?
If anyone adds a new kind of message later and forgets to copy those same few lines that carry the trace id across, that message's trace would just look like it starts fresh at the worker, with nothing telling them it used to be part of something bigger.
The alert is also only as good as its 30% threshold staying sane — and today I couldn't wait long enough to actually watch it go off, so I can't promise it fires, only that its condition and its wiring are both real.
