# Day 10 Task 1 — EF Core change tracker + AsNoTracking

## What the change tracker is

Every `DbContext` owns a `ChangeTracker`. When a tracked query materialises a row, EF Core
does two things beyond building the object: it keeps a reference to that object keyed by
its primary key (the **identity map**), and it stores a copy of the column values as they
were when read (the **original-values snapshot**). `SaveChanges()` walks the tracker,
diffs each tracked entity's current property values against its snapshot, and issues an
`UPDATE` only for the columns that changed. None of this happens for a query with
`.AsNoTracking()` — the entity is handed back and the context immediately forgets it.

## Identity resolution

Because the identity map is keyed by primary key, asking a **tracked** context for the
same key twice does not run a second materialisation — it returns the exact same object
reference that the first query already produced. Ask an **untracked** context for the same
key twice and there is no map to consult, so you get two independently materialised
objects with equal data but different identities.

`ChangeTrackerDemo.Tests/IdentityResolutionTests.cs` proves this with `Assert.Same` /
`Assert.NotSame`, and goes one step further: it mutates the first tracked instance's
`Name` in memory (no `SaveChanges()`), queries the same key again, and asserts the second
query returns that in-memory mutation. If EF Core had re-read the row, it would report the
original database value instead — getting the mutation back is direct evidence that the
second query never touched the database.

## Tracked vs. untracked `SaveChanges()`

`ChangeTrackerDemo.Tests/TrackedVsUntrackedSaveChangesTests.cs` demonstrates the practical
consequence: a tracked entity's field is changed and `SaveChanges()` is called; a **fresh**
`DbContext` (not the one that made the edit) re-reads the row and sees the new value. Doing
the identical edit through an `AsNoTracking()` entity and calling `SaveChanges()` writes
nothing — a fresh context re-reads the *original* value, because there was never a
snapshot for `SaveChanges()` to diff the edited instance against, so EF Core has no way to
know anything changed.

## Why a fresh `DbContext` per iteration is essential

`TrackingBenchmark.MeasureOnce` constructs a brand-new `CatalogContext` for every single
measured (and warmup) iteration, of both variants. If one context were reused across
iterations, the *second* tracked read of the same rows would hit the identity map from the
first read and skip materialisation entirely — making the tracked variant look artificially
fast and invalidating the entire comparison. A fresh context also means a fresh SQLite
connection, so both variants pay the identical "open a connection" cost every iteration,
which is another fairness requirement (same query, same ordering, same materialisation,
same row count — only `.AsNoTracking()` differs).

## Why `GC.Collect()` runs before each measured iteration

`GC.GetAllocatedBytesForCurrentThread()` reports a running total for the thread, not a
value scoped to the current iteration. Calling `GC.Collect()` and
`GC.WaitForPendingFinalizers()` immediately before taking the "before" allocation snapshot
clears out garbage left over from the *previous* iteration (discarded tracked entities,
finalizable connection objects, etc.) so it never gets billed to the iteration currently
being measured. This does not eliminate all noise, but it stops the by far largest source
of iteration-to-iteration cross-contamination.

## Measurement-method limitation

BenchmarkDotNet was deliberately not used (see `submission.md` for the reasoning). This
harness instead uses `System.Diagnostics.Stopwatch` and
`GC.GetAllocatedBytesForCurrentThread()`, both from the BCL. That means: single-process
measurements, no statistical outlier rejection, no per-run process isolation, and
susceptibility to whatever else is running on the machine at the time. Treat the numbers
in `output/results.json` as directionally honest, not laboratory-grade — BenchmarkDotNet
would produce tighter, more defensible numbers, at the cost of a new dependency this task
does not otherwise need.

## Database choice

A real, file-based SQLite database (not the EF Core InMemory provider, not a shared-cache
in-memory connection). The file lives under the OS temp directory
(`{TempPath}/changetrackerdemo-day10/catalog.db` for the benchmark run, a uniquely-named
temp file per test/fixture for the test suite) — never inside the repository — so it can
never be accidentally committed. A real file was chosen over a shared in-memory connection
because it is simpler to reason about "one fresh `DbContext` per iteration": the database
persists independently of any connection's lifetime, with no risk of the shared-cache
database being torn down if the wrong connection happens to close last.

## How to re-run everything

From `day-10/task-1/`:

```bash
# Seed 10,000 rows and run the timed benchmark; writes output/results.json
dotnet run --project ChangeTrackerDemo

# Run the demonstration + verification test suite (reads output/results.json,
# so run the benchmark above at least once first)
dotnet test Task1.slnx
```
