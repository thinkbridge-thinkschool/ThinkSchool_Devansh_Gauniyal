# Day 31 Task 1 — Polish: tests, perf, security

**CI run:** _[fill in after pushing — go to the repo's Actions tab, find the "Capstone CI" workflow run for this branch, and paste its URL here once it's green]_

## Test coverage per layer

Measured with `coverlet` (`--collect:"XPlat Code Coverage"`), one test project at a time so each number reflects only the layer that project exercises:

| Layer | Test project | Assembly | Line coverage |
|---|---|---|---|
| Domain | `Capstone.Invoicing.Domain.Tests` (29 tests) | `Capstone.Invoicing.Domain` | 87.7% |
| | | `Capstone.SharedKernel` | 44.1% |
| Application + Infrastructure | `Capstone.Integration.Tests` (12 tests, real SQL Server via Testcontainers) | `Capstone.Invoicing.Application` | 100% |
| | | `Capstone.Procurement.Application` | 100% |
| | | `Capstone.Invoicing.Infrastructure` | 89.1% |
| | | `Capstone.Procurement.Infrastructure` | 92.1% |
| API / HTTP | `Capstone.Web.Api.Tests` (9 tests, `WebApplicationFactory` + real SQL Server) | `Capstone.Web` | 38.5% |

`Capstone.Web`'s number is lower because Program.cs also holds the demo endpoints and telemetry/DI wiring that today's new tests don't target, not because the `/v1/*` invoice endpoints themselves are thinly covered.

## Hot-path perf pass

Seeded a local SQL Server (Testcontainers, 50,000 rows in `Invoices`) and ran identical `bombardier -c 20 -d 15s -l` load against the three real candidate endpoints. `GET /v1/invoices` (paginated list) came back at p99 508ms, against 5.76ms for `GET /v1/invoices/{id}` and 36.13ms for `POST /v1/invoices` — a ~90x gap. Root cause: `InvoiceEfRepository.ListAsync`'s `OrderBy(SubmittedAt).Skip().Take()` had no supporting index, so SQL Server sorted the full table on every page request. Added `IX_Invoices_SubmittedAt` (and `IX_Invoices_Status`, the same gap on the column the deemed-approval sweep filters by) via a new migration.

**Before / after, identical load parameters (`bombardier -c 20 -d 15s -l`, `GET /v1/invoices?pageSize=50`, same 50,000-row dataset):**

| | p50 | p90 | p99 | Throughput |
|---|---|---|---|---|
| Before | 305.90ms | 424.20ms | 508.15ms | ~65 req/s |
| After | 2.69ms | 4.99ms | **19.33ms** | ~5,695 req/s |

Confirmed the index is what did it, not noise: `sys.dm_db_index_usage_stats` shows `IX_Invoices_SubmittedAt` with 93,870 `user_scans` after the "after" run; `SET STATISTICS IO` on the same query shows 3 logical reads.

**Which path was hottest and how that was determined:** `GET /v1/invoices` — measured directly (Day 26's existing telemetry predates every `/v1/*` endpoint, so it couldn't answer this), not assumed.

**Which tests run in CI versus local-only:** all four automated test projects (domain, architecture, integration, and the new WebApplicationFactory suite) run in CI — each is self-contained or backed by a Testcontainers SQL Server that needs no credential. Local/manual-only: the bombardier load test itself, the ZAP scan, and every live check against the real deployed Azure environment.

**Live project and snapshot:** the live project is `capstone/` — `day-31/task-1/` is a frozen snapshot of it as this day ended, never edited directly.

**Commit representing this day's state:** `f904be0a4fa868cbf4d608f3e58b4eebca839d3f`.

## What did you learn this session?
Running the app locally the normal way accidentally caught a real bug — a singleton depending on a scoped database connection — that had been quietly live in production since Day 29 without ever throwing, because production doesn't validate the DI graph the way local dev does by default. "It works when I deploy it" and "it's wired correctly" turned out not to be the same claim.

## What would break this?
Two people hitting the list endpoint the moment some other column I haven't indexed becomes the slow one — today only fixes the two columns I actually measured. Anything I didn't load-test with real data volume is still an unmeasured guess, not a proven fast path.
