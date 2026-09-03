# Provenance — Day 21 Task 1

## Source

Copied from `day-3/task-3` at commit `cc7fc3a58bbd78aedbbbc44f0222cb3c2e846e32`
("Add a real optional Author field to quotes, end to end") — the last commit that
touched `day-3/task-3`, and the commit at which this branch's base (`main`,
via `academy/main`) carries that folder. Verified identical to the working tree at
copy time with `diff -rq day-3/task-3 day-21/task-1` (excluding `bin`, `obj`, `*.db`,
this file, and the two path/file changes listed below).

`day-3/task-3` is frozen and was not modified to produce this copy — it cannot be:
six other tasks (`day-4/task-2`, `day-4/task-4`, `day-4/task-5`, `day-4/task-6`,
`day-4/task-7`, `day-11/task-1`) hold a cross-folder `ProjectReference` straight at
`day-3/task-3/QuotesApi/QuotesApi.csproj`, so any change to its DI, startup, or
public surface would ripple into those days' test suites. Precedent for copying
rather than editing in place already exists in this repo: `day-17/task-1/api` is
an earlier copy of the same project, made for the same reason (a same-day Azure
deployment needed its own mutable copy).

## Carried unchanged

Every `.cs` file under `QuotesApi/` and `QuotesApi.Tests/` — the auth setup
(internal JWT + Entra-style JWT, ownership authorization, refresh-token rotation),
the `Quotes/` in-memory repository, the `Performance/` sub-area
(`PerformanceDbContext`, `AuthorQuoteSummaryQuery`, `PerformanceSeeder`,
`SqlLogCollector`, `PerformanceDiagnosticsRunner`), and `Telemetry.cs` — none of
these files changed at all. Baseline behaviour was verified before any change was
made (see "Baseline" below).

## Modified from the source, and why

- `Task3.slnx` renamed to `Task1.slnx` — every other `day-N/task-M` folder in this
  repo names its solution file after its own task number; keeping `Task3.slnx`
  here would misname the copy. Contents (the two `<Project Path>` entries)
  unchanged.
- The source's `SUBMISSION.md` (Day 3 Task 3's own PR/CI-run links and mentor
  notes) was not carried forward — it describes a different day's grading
  submission and would be actively misleading left in this folder. Replaced by
  this task's own `submission.md`.
- `.gitignore` gained three lines (`*.db`, `*.db-shm`, `*.db-wal`) on top of the
  carried content — earlier days in this repo left stray untracked `.db` files;
  this task's own `appsettings.json` and `Program.cs` gained new keys/endpoints
  (see below), and `.gitignore` needed to cover the new `performance.db` this
  task's endpoints create.
- `appsettings.json` gained two new top-level sections (`Measurement`,
  `Redis`) — nothing existing in it was changed or removed.
- `Program.cs` gained new `using` statements, new service registrations (all
  appended, none of the existing ones touched), and a new endpoint block appended
  after the original `/api/authors/quote-summary` endpoint, which is otherwise
  untouched. The one genuine correctness fix inside the *new* code (not the
  carried code) is logged in the verification log below.

No file that existed in `day-3/task-3` was renamed, restyled, or refactored beyond
what's listed above. Everything below "Newly added" is new.

## Newly added for Day 21 Task 1

- `QuotesApi/Caching/DbQueryCounter.cs`, `CountingCommandInterceptor.cs`,
  `CountingPerformanceDbContext.cs`, `MeasurementOptions.cs`,
  `AuthorQuoteSummaryReader.cs`, `AuthorQuoteSummaryCacheService.cs` — the
  instrumentation and HybridCache wrapper. See README.md for what each does.
- `QuotesApi/wwwroot/demo.html` — the local browser demo page.
- `QuotesApi.Tests/CachingApiFactory.cs`, `CachingTests.cs` — 9 new xUnit tests.
- `scripts/run-measurement.sh`, `scripts/parse-measurement.cs` — the load-test
  driver and percentile parser (the latter reuses day-11's pattern, see below).
- `README.md`, `submission.md`, `output/` — this task's documentation and
  captured measurement evidence.

## Verification log

See `README.md`'s "Verification log" section for the two real bugs this task
caught and fixed (a `RemoveByTagAsync`-under-Redis-down 500, and a
non-`volatile` double-checked lock), plus a test-harness concurrency issue and a
test-only deadlock caught before either shipped. Kept in README.md rather than
duplicated here since it's the methodology document; this file stays focused on
lineage.

## Reused pattern

The load-test percentile math reuses the parsing pattern from
`day-11/task-1/QuotesApi.Performance.Tests/LatencyPercentileParser.cs` and
`day-11/task-2/FastApi.Tests/LatencyPercentileParser.cs` (both already in this
repo, both hand-verified there rather than reinvented here) — p50/p95/p99 are
computed from a real recorded latency list the same way, not estimated.

## Baseline (before any caching code was added)

- `dotnet restore Task1.slnx` — clean.
- `dotnet build Task1.slnx` — Build succeeded, 0 warnings, 0 errors.
- `dotnet test Task1.slnx` — **19/19 passed** (16 `[Fact]` + 1 `[Theory]` × 3
  `[InlineData]`), 0 failed, 0 skipped. This is the unmodified `day-3/task-3`
  auth test suite, running unchanged against the copy.
- `git status --porcelain day-3/task-3` — empty, confirmed both immediately
  after the copy and again after this build/test run.

## Final (after all of Day 21 Task 1's changes)

- `dotnet build Task1.slnx` — Build succeeded, 0 warnings, 0 errors.
- `dotnet test Task1.slnx` — **28/28 passed**, run 4 times consecutively with no
  flakiness (the original 19 unchanged, plus 9 new in `CachingTests.cs`).
- The required mutation check (see submission.md) broke 4 of the 9 new tests in
  the expected way, most notably the stampede test reporting `Expected: 51,
  Actual: 2040` (40 concurrent callers × 51 queries each, i.e. zero coalescing) —
  then all 28 passed again after reverting.
- `git status --porcelain day-3/task-3` — still empty.
