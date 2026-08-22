# Day 12 Task 2 — When to reach for Dapper

## What this is

The read query from [Day 12 Task 1](../task-1) — a "quote wall" that joins quotes to
authors and returns a flat, denormalized row per quote — reimplemented and measured three
ways over the same SQLite database:

1. **EF tracked** (`EfQueries.RunTracked`) — entities materialized with change tracking on.
2. **EF `AsNoTracking` projection** (`EfQueries.RunProjection`) — the fair EF baseline.
3. **Dapper** (`DapperQueries.Run`) — hand-written parameterized SQL mapped to the same DTO.

## What Dapper is, and what it deliberately does not do

Dapper is a micro-ORM: it maps the rows a `IDbConnection` command returns onto a plain
object, and nothing else. It does not track entities, does not generate SQL from LINQ, does
not manage migrations, and does not know the shape of your tables beyond the column names
in whatever query you hand it. Everything EF Core does automatically — change tracking,
query translation, schema management, compile-time-checked LINQ — Dapper leaves to you. That
trade is the entire subject of this task.

## Why comparing against a tracked EF query is unfair

A tracked EF query pays for two things the projection and Dapper never pay for: it
materializes full `Quote` and `Author` entities (every column, not just the ones the screen
needs), and it registers each of those entities with the change tracker so EF can later
detect edits and generate an `UPDATE`. Neither cost has anything to do with how fast EF Core
can execute and read back a projected result. Measuring Dapper against tracked EF answers
"is Dapper faster than change tracking a full entity graph you don't even use here" — a
question with an obvious answer that says nothing about EF versus Dapper. The fair question
is Dapper versus the query shape EF actually offers for exactly this situation:
`AsNoTracking()` plus a `Select` projection. That is why this task measures all three: the
tracked variant exists specifically to show how much larger the apparent win looks against
the wrong baseline.

## Why each hygiene rule exists

- **Fresh `DbContext` / fresh connection per iteration, for every variant.** A second query
  against an already-open connection, or a `DbContext` that already has its model cached and
  its connection warm, is faster than a first query for reasons that have nothing to do with
  EF versus Dapper. Every iteration of every variant starts cold, exactly like the others.
- **A discarded warmup iteration per variant.** The first call into EF Core pays JIT and
  query-compilation cost; the first call on a connection pays connection-open cost. Neither
  belongs in a comparison between the variants, since all three would pay it once and none
  would pay it again.
- **At least 5 measured iterations, every one reported, plus the median.** A single run on a
  shared laptop can be dominated by an unrelated background process. The median is resistant
  to one outlier iteration in a way a single measurement or a mean is not; reporting every
  individual run instead of only the summary lets a reader spot that outlier rather than
  trust a number they can't audit.
- **`GC.Collect()` and `GC.WaitForPendingFinalizers()` before every measured iteration.** A
  garbage collection pause that happens to land inside one variant's timing window (and not
  another's) would make the comparison about GC timing luck rather than the variant. Forcing
  a clean, comparable heap baseline before each measured iteration removes that source of
  noise from both the elapsed-time and the allocated-bytes figures.
- **Same database file, same seed, same row count for all three.** Otherwise a difference in
  results could come from a difference in data rather than a difference in code path.
- **Elapsed milliseconds AND allocated bytes, plus allocation per row.** Time alone hides
  where the cost comes from; allocation makes the change-tracking and entity-materialization
  overhead visible even when two variants' timings are close.
- **Interleaved iteration order, recorded.** Running all of variant A, then all of B, then
  all of C would let anything that warms up over time (JIT tiering, OS file-cache behaviour)
  systematically favour whichever variant ran last. This harness runs one iteration of every
  variant per round, rotating which variant starts each round, and records the exact
  per-round order in `output/results.json` under `IterationOrders`.

## What you give up by hand-writing SQL

- **Migrations.** EF Core's model drives `dotnet ef migrations`; a hand-written query has no
  relationship to that pipeline and does not get updated by it.
- **Change tracking.** Dapper has no concept of "this object came from the database and I
  should diff it on save." A write path built on Dapper has to build that itself, or not have
  it.
- **Compile-time-checked queries.** EF's LINQ is checked by the C# compiler against the
  entity model. A SQL string is checked by nothing until it runs — a column rename in the
  model does not break the Dapper query at compile time, it breaks it the first time that
  query actually executes.
- **Refactoring that follows the model.** Renaming `Quote.Text` via a rename refactor updates
  every LINQ query that references it. It does not touch the string literal in
  `DapperQueries.Sql`.

## Resolved interpretations, in full

1. **Which query.** Day 12 Task 1's quote-wall read — the highest-volume read path in the
   domain, already available in an optimized EF form, so reusing it keeps the comparison
   honest instead of hand-picking a query that flatters one side. Rebuilt fresh in
   `day-12/task-2` with the same entity shapes, same DTO shape, and same logical query, but no
   project reference to task-1.
2. **The fair-comparison requirement.** Three variants are measured, not two: EF tracked
   (the unfair baseline, included specifically to show why it's wrong), EF
   `AsNoTracking`+`Select` projection (the fair baseline), and Dapper. The headline comparison
   is projection versus Dapper. All three are asserted to return identical row counts and
   identical field-by-field data in identical order — see `EquivalenceTests.cs`.
3. **Measurement method.** BenchmarkDotNet was deliberately not used — it is the professional
   tool for this job, but it is not one of the named or unavoidable additions this task
   permits. `System.Diagnostics.Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()`
   (both BCL) measure instead, with the hygiene rules above applied by hand. This is a
   single-process measurement on a shared laptop without statistical rigour beyond reporting
   every run and the median; BenchmarkDotNet would give tighter numbers with proper
   statistical analysis, process isolation, and outlier detection.
4. **The two "What this builds" tags** ("CQRS with MediatR", "`Span<T>` + memory
   primitives") are topic labels for the day, not deliverables — neither the task body nor
   the exercise asks for either. No MediatR and no `Span<T>`/memory-pooling code was written.
5. **The Dapper implementation.** Parameterized (`@SubmittedSinceUtc`, bound via Dapper's
   anonymous-object parameter binding — never string interpolation or concatenation into the
   SQL), opens and disposes its own `SqliteConnection` per call, and maps into the exact same
   `QuoteWallItem` DTO the EF projection returns. The query carries a `WHERE CreatedAt >=
   @SubmittedSinceUtc` filter with the cutoff set to `2000-01-01`, years before any seeded
   row — a genuine, real parameter (needed to satisfy the requirement that the SQL actually
   be parameterized, and realistic for a quote wall, which would very plausibly take a
   "since" cutoff in practice) that never excludes a row against this seed, so it does not
   change the logical query or the row count. The same filter is applied identically to both
   EF variants, so all three remain the same logical query.
6. **The teammate rule.** See "Notes for mentor" in `submission.md` — written from the actual
   measured numbers in `output/results.json`, not from general advice.
7. **Evidence.** Every number and every SQL statement quoted in `submission.md` and here
   comes from `output/results.json`, `output/ef-tracked-sql.log`, `output/ef-projection-sql.log`,
   and `output/dapper-sql.log`, all generated by a real run of
   `dotnet run --project DapperComparison -- run-comparison`. `EnableSensitiveDataLogging()`
   is development-only — it writes real parameter values into the log — and every value it
   logs here is the synthetic `2000-01-01` cutoff constant; no seed data leaks into it because
   the query has no other parameters.

## Why 10,000 rows

100 authors x 100 quotes = 10,000 rows. Small enough that seeding and testing stay fast in
CI (well under a second), large enough that the cost of materializing and tracking 10,000
entities becomes clearly visible above single-process measurement noise on a laptop —
task-1's 60-row seed would be too small to measure a meaningful difference at all.

## How to run everything

From `day-12/task-2/`:

```bash
# Build
dotnet build Task2.slnx

# Run the measurement harness and capture evidence to output/
dotnet run --project DapperComparison -- run-comparison output/dappercomparison.db output

# Run the full test suite
dotnet test Task2.slnx
```
