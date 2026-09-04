# Provenance — Day 22 Task 1

## Source

Copied from `day-21/task-1` at commit `d9b98448ef4720daf88cb10156098f3af203697b`
("Fix Day 21 Task 1 README: dotnet run needs ASPNETCORE_ENVIRONMENT=Development") —
the last commit that touched `day-21/task-1`, now merged into `academy/main` via
PR #44. Verified identical to the working tree at copy time with `diff -rq
day-21/task-1 day-22/task-1` (excluding `bin`, `obj`, `*.db`, this file, and the
content-replacement changes listed below).

`day-21/task-1` is frozen and was not modified to produce this copy, and neither
was `day-3/task-3` underneath it (six other tasks — `day-4/task-2,4,5,6,7`,
`day-11/task-1` — hold a `ProjectReference` straight at `day-3/task-3/QuotesApi`,
and `day-21/task-1` is itself a copy-forward of that same project with Day 21's
caching added). This is the second copy-forward in that lineage:
`day-3/task-3` → `day-21/task-1` → `day-22/task-1`.

Day 22 is a deliberate continuation of Day 21, not an independent exercise: Day
21's `HybridCache` + Redis L2 setup is exactly the kind of outbound dependency
Day 22's brief asks to wrap in resilience, so this task adds resilience to the
same running app rather than building a new one from scratch.

## Carried unchanged

Every `.cs` file under `QuotesApi/` and `QuotesApi.Tests/` that existed in
`day-21/task-1` **except `AuthorQuoteSummaryCacheService.cs`** (see "Modified"
below) — the full auth setup, the in-memory `Quotes/` repository, the
`Performance/` sub-area, `Telemetry.cs`, `Caching/DbQueryCounter.cs`,
`CountingCommandInterceptor.cs`, `CountingPerformanceDbContext.cs`,
`MeasurementOptions.cs`, `AuthorQuoteSummaryReader.cs`, plus
`QuotesApi.Tests/AuthApiFactory.cs`, `AuthIntegrationTests.cs`,
`CachingApiFactory.cs`, `CachingTests.cs`, `GlobalUsings.cs`. Also carried,
unused by Day 22 but left in place rather than deleted since they're still
valid, working Day 21 tooling: `scripts/run-measurement.sh`,
`scripts/parse-measurement.cs` (Day 21's load-test driver and percentile
parser). None of these files changed at all — Day 21's caching behaviour was
verified working in the copy *before* any resilience code was added (see
"Baseline" below).

## Modified from the source, and why

- `Program.cs` gains new `using` statements, new service registrations
  (resilience pipelines, fault-injection switches, the HTTP dependency client),
  and new endpoint blocks — all appended after Day 21's existing endpoints,
  none of which are touched. Redis's `IDistributedCache` registration is
  wrapped in a resilience-and-fault-injection decorator (see README.md); the
  registration call itself is additive, not a rewrite of Day 21's options.
- `QuotesApi/Caching/AuthorQuoteSummaryCacheService.cs` (carried from Day 21)
  gained one additional `catch` type in `EvictAsync`:
  `Polly.CircuitBreaker.BrokenCircuitException`, alongside the
  `RedisConnectionException` Day 21 already caught there. Necessary, not
  optional: once Day 22 puts a circuit breaker in front of Redis, an *open*
  breaker surfaces as `BrokenCircuitException`, which Day 21's catch clause
  never covered — confirmed live (opening the Redis breaker and calling
  `POST /api/measurement/reset` 500'd until this was added). See README.md's
  verification log for the full account. No other line in this file changed.
- `QuotesApi/wwwroot/demo.html` (Day 21's cache demo) is extended, not
  replaced — Day 21's cached/uncached concurrency demo and cold/warm latency
  panel are unchanged; a second section for the resilience/breaker demo is
  added below them on the same page.
- `submission.md` and `README.md` (Day 21's own content) are replaced with
  Day 22's own — carrying Day 21's submission notes forward under a Day 22
  heading would misrepresent which task they document.
- `output/` — Day 21's own load-test evidence (bombardier reports, DB-query
  counts, `summary.md`) was **not** carried forward; it documents Day 21's
  measurement exercise, not Day 22's, and leaving stale numbers from a
  different day's exercise in this task's evidence folder would be
  confusing. Repopulated with Day 22's own breaker-lifecycle, bulkhead,
  timeout, and retry-backoff evidence (Phase 8).
- `.gitignore` unchanged from Day 21 (already covers `*.db`/`*.db-shm`/
  `*.db-wal`/`bin`/`obj`); no new pattern needed yet at copy time.

No file that existed in `day-21/task-1` was renamed. `Task1.slnx` keeps its
name unchanged (day-21/task-1 was itself `task-1`, so no rename was needed
this time, unlike the day-3/task-3 → day-21/task-1 copy which did rename
`Task3.slnx`).

## Newly added for Day 22 Task 1

- `QuotesApi/Resilience/`: `FaultMode.cs`, `FaultInjectionSwitch.cs`,
  `InjectedFaultException.cs`, `DependencyKeys.cs`, `ResilienceTuningOptions.cs`,
  `ResilientDistributedCache.cs` (the Redis decorator),
  `RedisResiliencePipelineConfiguration.cs`, `HttpResiliencePipelineConfiguration.cs`,
  `ExternalServiceClient.cs`, `ExternalServiceCallCounter.cs`. See README.md
  for what each does.
- New endpoints in `Program.cs`: `GET /api/external/quote-of-the-day` (the
  controllable dependency itself), `GET /api/resilience/external/call`,
  `GET /api/resilience/redis/call`, `GET|POST /api/faults/{dependency}`,
  `GET /api/resilience/breakers`, `GET /api/resilience/external/call-count`
  (+ its reset).
- `QuotesApi.Tests/ScriptedHandler.cs`, `CapturingLoggerProvider.cs` — copied
  from `day-5/task-6/ResilienceDemo.Tests` (namespace adjusted only; see README.md
  "how this goes beyond day-5/task-6").
- `QuotesApi.Tests/ResilienceApiFactory.cs`, `ResilienceTests.cs` — 11 new xUnit
  tests.
- `scripts/capture-resilience-evidence.sh` — the breaker-lifecycle/bulkhead/
  timeout/retry evidence capture script.
- `README.md`, `submission.md`, `output/` — this task's documentation and
  captured resilience evidence.

## Baseline (before any resilience code was added)

- `dotnet restore Task1.slnx` — clean.
- `dotnet build Task1.slnx` — Build succeeded, 0 warnings, 0 errors.
- `dotnet test Task1.slnx` — **28/28 passed**, 0 failed, 0 skipped. This is the
  unmodified Day 21 suite (19 auth tests + 9 caching tests), running unchanged
  against the copy.
- Live-verified Day 21's caching in the running copy (Redis already up from
  Day 21, reused as-is): `POST /api/measurement/reset` → 0;
  `GET /api/authors/quote-summary/cached?key=copycheck` (cold) →
  `db-query-count` = 51 (the real 1+N query shape); a second call against the
  same key → `db-query-count` still 51 (served from cache, no new DB round
  trip). Exactly Day 21's documented behaviour.
- `git status --porcelain day-21/task-1` and `git status --porcelain
  day-3/task-3` — both empty, confirmed immediately after the copy and again
  after this build/test/live-check.

## Final (after all of Day 22 Task 1's changes)

- `dotnet build Task1.slnx` — Build succeeded, 0 warnings, 0 errors.
- `dotnet test Task1.slnx` — **39/39 passed** (the original 28 from Day 21 —
  19 auth + 9 caching, including the stampede-protection test — unchanged,
  plus 11 new resilience tests), run repeatedly with no flakiness.
- The required mutation check (see submission.md) — removing the Redis circuit
  breaker's registration entirely — broke the graceful-degradation test in the
  expected way (`Expected: "Open", Actual: "Closed"`), then all 39 passed again
  after reverting.
- `git status --porcelain day-21/task-1` and `git status --porcelain
  day-3/task-3` — still both empty.
