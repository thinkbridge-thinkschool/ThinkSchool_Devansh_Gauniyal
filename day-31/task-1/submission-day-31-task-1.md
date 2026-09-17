# Day 31 Task 1 — Polish: tests, perf, security

## CI run

**CI run link:** _[fill in after pushing — go to the repo's Actions tab, find the "Capstone CI" workflow run for this branch, and paste its URL here once it's green]_

## Test coverage per layer

Measured with `coverlet` (`--collect:"XPlat Code Coverage"`), one test project at a time so each number reflects only the layer that project actually exercises:

| Layer | Test project | Assemblies | Line coverage |
|---|---|---|---|
| Domain | `Capstone.Invoicing.Domain.Tests` (29 tests) | `Capstone.Invoicing.Domain` | 87.7% |
| | | `Capstone.SharedKernel` | 44.1% |
| Application + Infrastructure | `Capstone.Integration.Tests` (12 tests, real SQL Server via Testcontainers) | `Capstone.Invoicing.Application` | 100% |
| | | `Capstone.Procurement.Application` | 100% |
| | | `Capstone.Invoicing.Infrastructure` | 89.1% |
| | | `Capstone.Procurement.Infrastructure` | 92.1% |
| API / HTTP | `Capstone.Web.Api.Tests` (9 tests, `WebApplicationFactory` + real SQL Server) | `Capstone.Web` | 38.5% |

`Capstone.Web` sits lowest because Program.cs also holds the demo endpoints (`/demo/*`), OpenTelemetry/Key-Vault wiring, and both background services' startup registration — none of which today's new HTTP tests target, and none of which is invoice-lifecycle logic. The `/v1/*` endpoints and their error paths are the part actually covered.

## Hot-path perf pass

**Which path was hottest, and how that was determined:** Day 26's own telemetry (`observability/results/01-latency-p50-p99-by-endpoint.json`) predates real persistence and every `/v1/invoices` endpoint, so it couldn't answer this — measured fresh instead. Seeded a local SQL Server (Testcontainers image, 50,000 rows in the `Invoices` table) and ran identical `bombardier -c 20 -d 15s -l` runs against the three real candidate paths. `GET /v1/invoices` (the paginated list) came back at **p99 508ms**, against **5.76ms** for `GET /v1/invoices/{id}` and **36.13ms** for `POST /v1/invoices` — a ~90x gap, so the list endpoint was the clear, measured answer, not a guess.

**Root cause:** `InvoiceEfRepository.ListAsync` does `OrderBy(SubmittedAt).Skip().Take()` with no supporting index — SQL Server had to sort the entire 50,000-row table on every single page request, however small the page.

**Fix:** added `IX_Invoices_SubmittedAt` (and, for the identical unindexed-scan shape on the same table, `IX_Invoices_Status`, used by the deemed-approval sweep) via a new EF Core migration.

**Before / after, same load parameters (`bombardier -c 20 -d 15s -l`, `GET /v1/invoices?pageSize=50`, 50,000-row dataset, local SQL Server via Testcontainers):**

| | p50 | p90 | p99 | Throughput |
|---|---|---|---|---|
| Before | 305.90ms | 424.20ms | 508.15ms | ~65 req/s |
| After | 2.69ms | 4.99ms | **19.33ms** | ~5,695 req/s |

Confirmed the improvement is actually the index, not noise: `sys.dm_db_index_usage_stats` after the "after" run shows `IX_Invoices_SubmittedAt` with 93,870 `user_scans` and the PK index doing far less work; `SET STATISTICS IO` on the same query shows 3 logical reads.

A second, real finding surfaced while setting this up (not part of the perf pass itself, fixed because it blocked a meaningful concurrent load test and is a genuine correctness bug): `IPurchaseOrderCapacityGateway`/`IPurchaseOrderCapacityPort` were registered `Singleton` over a `Scoped` EF repository — a captive dependency, silently live since Day 29, that only surfaces because `dotnet run`'s Development-mode DI validation catches it and a deployed Production-mode host does not. Fixed to `Scoped`. See the self-review below.

**Which tests run in CI versus local-only:** Every automated test (`Capstone.Invoicing.Domain.Tests`, `Capstone.ArchitectureTests`, `Capstone.Integration.Tests`, `Capstone.Web.Api.Tests`) runs in CI — all four are self-contained, either pure in-memory or backed by a Testcontainers SQL Server that GitHub's `ubuntu-latest` runners can start with no credential. Local/manual-only: the bombardier load test itself (a load-generation tool, not part of the test suite), the ZAP scan against the real deployed URL, and every live verification against the actual Azure environment (migration application, 401 checks, zero-secrets confirmation) — none of those can run without a real Azure credential this workflow deliberately has none of.

## Security re-check

Re-ran Day 27's exact ZAP baseline (`docker run ghcr.io/zaproxy/zaproxy:stable zap-baseline.py`) against `capstone-dev-api-f7nsoj.azurewebsites.net`. Day 27's final baseline: one informational-only finding, "Non-Storable Content" (2 instances — the correct side effect of the `no-store` `Cache-Control` fix, not a defect). Today's re-check: the identical single finding, now 3 instances simply because Days 29-30 added real endpoints for ZAP's crawler to walk. No new alert type — no regression from real persistence, six new endpoints, or two new background services.

Day 25's zero-secrets property and Day 27's hardening both reconfirmed live today: `az webapp config appsettings list` shows every value is a non-secret identifier/hostname/Key-Vault-reference; `az sql server show` shows `publicNetworkAccess: Disabled`.

## Live project and snapshot

The live project is `capstone/` — `day-31/task-1/` is a frozen snapshot of it as this day ended, never edited directly.

Commit representing this day's state: `[fill in below once finalized]`.

## What did you learn this session?
Running the app locally the normal way (`dotnet run`, Development mode) accidentally caught a real bug — a singleton depending on a scoped database context — that had been quietly live in production since Day 29 without ever throwing, because production doesn't validate the DI graph the way local dev does by default. I hadn't appreciated before today that "it works when I deploy it" and "it's wired correctly" aren't the same claim.

## What would break this?
Two people hitting the list endpoint at the exact moment the invoice table has grown huge again in some other way I haven't indexed for yet — today only fixes the two columns I actually measured being slow. Anything I didn't load-test with real data volume is still an unmeasured guess, not a proven fast path.
