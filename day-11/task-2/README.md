# Day 11 Task 2 — Drop p99 by 10×

Fixes the N+1 and missing-index anti-patterns task-1 deliberately introduced and
measured, then re-measures under the same load to see the real improvement. This is a
fresh, self-contained project (`FastApi`) that reproduces task-1's model, seed and
endpoint contract exactly - task-1 itself, and its captured baseline evidence, are
untouched.

## The three endpoints

- **`GET /authors/quote-summary/slow`** - task-1's exact N+1 query, reproduced unmodified,
  so the in-process tests can prove the fix (not a changed workload) is what makes the
  difference.
- **`GET /api/authors/quote-summary`** (PRIMARY) - the **projection** fix: a single
  `Select` into `AuthorWithQuotesDto`, where `a.Quotes.Count()` becomes a correlated
  scalar subquery. One SQL statement total. Because the result is projected straight into
  a DTO rather than an entity, EF Core's change tracker never gets involved - nothing to
  track means nothing to diff on `SaveChanges()`, which is the same tracking cost Day 10
  Task 1 measured directly.
- **`GET /authors/quote-summary/split`** - the **Include with split queries** fix:
  `Include(a => a.Quotes).AsSplitQuery()`. Two SQL statements total, regardless of author
  count.

## Why split query exists, and what cartesian explosion is

A plain `Include(a => a.Quotes)` with no split loads authors and quotes in a single JOIN.
For a one-to-many relationship, that JOIN produces one result row per *child*, not per
parent - so 50 authors with 100 quotes each comes back as 5,000 rows, each repeating its
author's columns 100 times over. That's the cartesian explosion: the result set balloons
to parent-count × child-count before EF Core can even start reassembling it back into 50
author objects. `AsSplitQuery()` avoids this by issuing two separate, compact queries
instead - one for authors, one for their quotes, correlated by the buffered author IDs -
trading one bloated query for two cheap ones.

## Why the comparison must hold every other variable constant

If the after-measurement used a different concurrency, a different duration, a different
row count, or a trimmed response shape, an apparent 10× improvement could just as easily
be "the workload got easier" rather than "the fix worked." Every one of those variables -
tool, version, concurrency, duration, seed data, endpoint route, response field set,
warmup procedure - is confirmed identical to task-1's committed baseline before the
re-measurement even starts; `scripts/run-profile.sh` asserts several of these live and
fails loudly rather than silently proceeding on a mismatch.

## Why the fix is measured, not assumed

"Projection is faster" and "split query avoids cartesian explosion" are both true in
general, but *how much* faster, for *this* workload, on *this* machine, is an empirical
question - not something to state from first principles. The real p50/p99, the real SQL,
and the real `EXPLAIN QUERY PLAN` output in `output/` are what actually back the numbers
in `submission.md`, not a theoretical estimate.

## How to re-run everything

Requires the .NET 10 SDK and `bombardier` on `PATH` (this repo already has it from
task-1).

```bash
cd day-11/task-2
scripts/run-profile.sh
```

This verifies its load parameters against task-1's committed baseline first (failing
loudly on any mismatch), builds the solution, starts the API on a free local port, warms
up and load-tests both fixed endpoints with parameters identical to task-1, and captures
the single-request SQL log, query plan, and schema dump for each - all into `output/`. It
always stops the API afterwards, even if a step fails.

To run just the tests (no load test, no running API required):

```bash
cd day-11/task-2
dotnet test Task2.slnx
```

The `ArtefactTests` in `FastApi.Tests` read the files under `output/` (and, read-only,
task-1's committed `output/`) and will fail until `scripts/run-profile.sh` has been run at
least once - that is expected, not a bug.
