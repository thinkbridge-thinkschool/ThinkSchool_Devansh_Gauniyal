## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-11/task-1/day-11/task-1

(The endpoint itself lives in `day-3/task-3/QuotesApi/Performance/` on this same
`day-11/task-1` branch - see "Where the code actually lives" below.)

## Notes for mentor

### The Week-1 API question - and how the decision changed mid-task

The task asks to add the slow endpoint to "your Week-1 API". This repo's Week-1 API is
`day-3/task-3/QuotesApi`. The first pass at this task built a separate, self-contained
stand-in project instead of touching it, reasoning that `QuotesApi` was frozen, referenced
by five day-4 test projects, and had no EF Core or authors/quotes relationship at all. That
reasoning wasn't wrong, but on review it was still a deviation from what the task literally
asked for, and I was directed to redo it against the real API instead. This submission
reflects that: the slow endpoint is now added directly to `day-3/task-3/QuotesApi`.

### Where the code actually lives

The new endpoint and its supporting model, `DbContext`, seeder and diagnostics code live
under `day-3/task-3/QuotesApi/Performance/` - a new, additive-only subfolder. Nothing
pre-existing in `QuotesApi` was changed in behavior; the only edits to existing files were
adding one package reference (`Microsoft.EntityFrameworkCore.Sqlite`) to `QuotesApi.csproj`
and, in `Program.cs`, one new `using`, one new CLI-argument branch for a standalone
diagnostics mode, and one new endpoint mapped after every existing route. This
`day-11/task-1` folder holds only the tests, the profiling script, the README, and the
captured evidence - it does not hold the endpoint's source.

This is a real structural break from every other day in this repo, where each day's work
is fully self-contained in its own folder. It also means the change exists only on the
`day-11/task-1` branch: the actual `day-3/task-3` branch on GitHub is untouched, since git
branches are independent snapshots. Anyone browsing `day-3/task-3` still sees the original,
unmodified Day 3 submission; the modified copy of that same file path only shows up when
browsing the `day-11/task-1` branch specifically.

### Regression risk this created, and how it was verified

`day-3/task-3/QuotesApi`'s `Program.cs` is referenced directly (not copied) by
`day-4/task-2`, `task-4`, `task-5`, `task-6`, and `task-7` - confirmed from their build
output before touching anything. The new endpoint's database seeding is lazy (it only
runs the first time `/api/authors/quote-summary` is actually called), specifically so none
of those five projects' existing tests are affected merely by booting the app via
`WebApplicationFactory`, which none of them do differently. After the change:
`day-3/task-3` (19/19), `day-4/task-2` (37+19), `task-4` (2+37+19), `task-5` (2+2+37+19),
`task-6` (1+2+2+37+19), and `task-7` (12+1+2+2+37+19) all passed - matching their Phase 2
baseline counts exactly. One run of `day-4/task-7/QuotesApi.Options.Tests` did fail once
with an unrelated `ObjectDisposedException` from a `WebApplicationFactory` startup test;
repeating it 20 more times (isolated and at full-solution granularity, both with and
without this change) reproduced it zero more times, including 5/5 clean runs of the exact
original unmodified code. That points to pre-existing test flakiness in that class, not a
regression from this change - recorded here rather than hidden, since it's the kind of
thing a mentor should be able to challenge.

### How the app was actually run for the load test

`QuotesApi` validates several configuration sections eagerly at startup (an internal JWT
signing key, internal caller credentials) that are normally supplied via .NET
user-secrets, which I never read or touched. `scripts/run-profile.sh` instead generates
its own fresh, random, throwaway values every run via `openssl rand -base64`, passed only
as environment variables to the child process for its lifetime - never written to disk,
never committed, unrelated to any real secret. The app runs with
`ASPNETCORE_ENVIRONMENT=Testing`, which is also what every WebApplicationFactory-based
test in this repo already uses, so it skips Key Vault/Azure Monitor resolution the same
way those tests do.

### Resolved interpretations (all recorded so a mentor can challenge them)

1. **Database and plan format.** EF Core with SQLite, natively on arm64, no container -
   matching Day 5 and Day 10. The plan evidence below comes from SQLite's `EXPLAIN QUERY
   PLAN`, which is genuinely sufficient to show a missing index (a full `SCAN` before an
   index would give a `SEARCH`), but it is **not** a SQL Server actual execution plan as
   captured in Day 8 - it carries far less detail. WAL mode is enabled
   (`PRAGMA journal_mode=WAL`) so the load test's concurrent readers and the separate
   single-request diagnostics capture don't contend on SQLite's single writer lock.
2. **Schema.** `Author` (int key, name, country) and `AuthorQuote` (int key, text,
   `AuthorId` FK, created date) - named `AuthorQuote`, not `Quote`, specifically to avoid
   colliding with the existing `QuotesApi.Quotes.Quote` record used by the real auth/CRUD
   endpoints. One-to-many, seeded deterministically: exactly 50 authors, 100 quotes each,
   5,000 quotes total. All data is synthetic (`Author 003`, `Synthetic quote text 00042`,
   countries like `Testonia`/`Fixturia`) - no real people or real quotations. Real seeded
   counts, confirmed by `SeedingTests`: **50 authors, 5,000 quotes**.
3. **How the endpoint is deliberately slow - two required problems:**
   - **(a) N+1.** `AuthorQuoteSummaryQuery.Run` loads all authors with one query, then
     inside the loop calls `context.Entry(author).Collection(a => a.Quotes).Load()` for
     each author - an explicit per-author load, not `Include()`. Verified from the
     captured SQL log: **51 executed statements for one request** (1 + 50).
   - **(b) Missing index.** `AuthorQuote.AuthorId` has no index. EF Core creates one on a
     required FK by convention, so it had to be explicitly suppressed - done by removing
     `ForeignKeyIndexConvention` in `PerformanceDbContext.ConfigureConventions`. (A first
     attempt calling `entity.Metadata.RemoveIndex(...)` inside `OnModelCreating` looked
     right but was a no-op: at that point in the builder pipeline the convention hasn't
     created the index yet, so there was nothing to remove, and it got created anyway
     during model finalization. Removing the convention itself is the fix, confirmed
     against the real created schema.) Verified: `schema-dump.txt` shows **no index on
     the Quotes table**, and `EXPLAIN QUERY PLAN` on the per-author query shows a plain
     **`SCAN`**, not a `SEARCH`.

   No `Thread.Sleep` or artificial delay anywhere - the slowness is entirely these two
   anti-patterns.
4. **Scope - measure only.** Nothing was fixed. No `Include()`, no projection, no index
   migration. The endpoint stays slow after this task.
5. **Load test parameters.** `bombardier -c 20 -d 10s -l http://127.0.0.1:5187/api/authors/quote-summary`,
   preceded by a discarded warmup (`bombardier -c 10 -d 3s`) so JIT and first-connection
   cost don't land in the reported percentiles. Machine: a single Apple Silicon (arm64)
   laptop running .NET 10 (`10.0.302`, RID `osx-arm64`), with the API and bombardier
   sharing the same CPU cores - not a client/server split. Absolute milliseconds are
   therefore not comparable to a production measurement; the *shape* (p99 far above p50)
   is the finding.
6. **How the SQL was captured.** `PerformanceDbContext.LogTo(...)` into `SqlLogCollector`
   plus `EnableSensitiveDataLogging()`, exactly as established in Day 10 Task 2.
   `EnableSensitiveDataLogging` is development-only - it writes real parameter values
   into the log instead of masking them. Every value in every captured file here is
   synthetic seed data (author names like `Author 007`, quote text like `Synthetic quote
   text 00042`, integer author IDs) - confirmed by inspection and by `SecretScanTests`.
   The SQL sample below comes from **one** representative in-process request (via
   `scripts/run-profile.sh`'s `performance-diagnostics` step, which builds no web host at
   all); the percentiles below come from the separate load run against the real running
   API - never mixed.
7. **Evidence.** Every number below is copied verbatim from a file under `output/`,
   produced by a real run of `scripts/run-profile.sh` against the real `QuotesApi` on
   2026-08-21. Nothing here is invented.

### Baseline p50 / p99

Command (from `output/load-test.txt`):
```
bombardier -c 20 -d 10s -l http://127.0.0.1:5187/api/authors/quote-summary
```
Tool: **bombardier version 2.0.2 darwin/arm64**. Concurrency: 20 connections. Duration:
10s (not a fixed request count). Machine: one arm64 laptop, API and load generator
sharing CPU.

Verbatim result:
```
Statistics        Avg      Stdev        Max
  Reqs/sec       410.84     225.26    1387.18
  Latency       49.04ms    37.97ms   349.00ms
  Latency Distribution
     50%    42.24ms
     75%    49.41ms
     90%    64.71ms
     95%   132.73ms
     99%   215.81ms
  HTTP codes:
    1xx - 0, 2xx - 4083, 3xx - 0, 4xx - 0, 5xx - 0
    others - 0
  Throughput:     1.54MB/s
```
**p50 = 42.24ms, p99 = 215.81ms** - the p99 is roughly 5.1x the p50, over 4,083 successful
requests, all 2xx.

### Offending SQL

Endpoint source (`day-3/task-3/QuotesApi/Performance/AuthorQuoteSummaryQuery.cs`):
```csharp
public static List<AuthorQuoteSummary> Run(PerformanceDbContext context)
{
    var authors = context.Authors.OrderBy(a => a.Id).ToList(); // query 1

    var summaries = new List<AuthorQuoteSummary>(authors.Count);
    foreach (var author in authors)
    {
        context.Entry(author).Collection(a => a.Quotes).Load(); // query 2..N+1
        summaries.Add(new AuthorQuoteSummary(author.Id, author.Name, author.Country, author.Quotes.Count));
    }

    return summaries;
}
```

Real logged SQL from `output/sql-sample.log` (one representative request - **51 executed
statements total**, 1 for authors + 50 per-author; first two shown, remaining 49
per-author statements trimmed - identical shape, only the `AuthorId` parameter changes):
```
--- statement 1 of 51 ---
Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "a"."Id", "a"."Country", "a"."Name"
      FROM "Authors" AS "a"
      ORDER BY "a"."Id"

--- statement 2 of 51 ---
Executed DbCommand (1ms) [Parameters=[@p='1'], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."AuthorId", "q"."CreatedAt", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."AuthorId" = @p

--- statement 3 of 51 ---
Executed DbCommand (0ms) [Parameters=[@p='2'], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."AuthorId", "q"."CreatedAt", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."AuthorId" = @p

... (statements 4 through 51 omitted here: same shape, @p = 3 through 50) ...
```
(49 more per-author statements follow in the full file, `output/sql-sample.log`.)

### The plan

Real `EXPLAIN QUERY PLAN` output (`output/query-plan.txt`) against the exact per-author
SQL EF Core generated above:
```
EXPLAIN QUERY PLAN against the exact per-author SQL EF Core generated for this request:
SELECT "q"."Id", "q"."AuthorId", "q"."CreatedAt", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."AuthorId" = @p

Plan output (id | parent | notused | detail):
2 | 0 | 216 | SCAN q
```

Schema dump (`output/schema-dump.txt`) proving no index exists on `AuthorId`:
```
CREATE TABLE statement for Quotes:
CREATE TABLE "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "Text" TEXT NOT NULL,
    "AuthorId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Quotes_Authors_AuthorId" FOREIGN KEY ("AuthorId") REFERENCES "Authors" ("Id") ON DELETE CASCADE
)

Indexes that exist on the Quotes table (name | sql):
(no indexes found on the Quotes table)
```

### The two biggest problems

1. **N+1 round-tripping.** One request to `GET /api/authors/quote-summary` makes **51
   separate round trips** to the database instead of 1 (evidence: `output/sql-sample.log`,
   and `NPlusOneTests.Endpoint_data_access_executes_one_plus_n_queries`, which asserts the
   real captured count equals `AuthorCount + 1`).
2. **Missing foreign-key index.** `AuthorQuote.AuthorId` has no index, so each of those 51
   queries that filters by it is a full table scan (evidence: `output/query-plan.txt`
   showing `SCAN q`, and `output/schema-dump.txt` showing zero indexes on `Quotes`).

They compound multiplicatively, not additively: N queries each doing a full scan is N
times the table, not N cheap indexed lookups. That combination is exactly the shape in the
load test - a p50 of 42ms next to a p99 of 216ms, because most requests land on a
lightly-loaded scan and some land during contention with a 5x-worse tail.

## What did you learn this session?

Removing the FK index inside `OnModelCreating` silently failed since the convention
creating it hadn't run yet - only the real schema dump caught it, not the model code.

## What would break this?

More authors scales this worse, since query count is N+1 and each is a full scan. These
numbers also share one CPU with the load generator, so a real client would see a worse tail.
