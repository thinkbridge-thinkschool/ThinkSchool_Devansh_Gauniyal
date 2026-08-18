# Day 8 Task 2 — Covering indexes + included columns

Proves, from real captured execution plans, that adding `INCLUDE` columns
to a non-clustered index eliminates a Key Lookup for a specific query —
using SQL Server 2022 in Docker, not an estimate.

This is a separate, self-contained experiment from Task 1: different
database (`CoveringLab`, not `IndexLab`), one query, one index, two stages.
Nothing here references or depends on `day-8/task-1`.

## Schema

Database `CoveringLab`, single table `dbo.Orders`:

| Column      | Type          | Notes                                       |
|-------------|---------------|----------------------------------------------|
| OrderId     | INT IDENTITY  | surrogate key, PRIMARY KEY CLUSTERED          |
| CustomerId  | INT           | ~5,000 distinct values, ~20 rows per customer |
| OrderDate   | DATETIME2(0)  | spread across a 5-year window                 |
| Status      | VARCHAR(20)   | skewed status values                          |
| Amount      | DECIMAL(10,2) | synthetic amount                              |
| Description | CHAR(300)     | fixed-width padding for predictable page density |

~100,000 rows, generated deterministically from row-number arithmetic (no
`NEWID()`/`RAND()`), so re-running the load produces byte-identical data.

The clustered index (on `OrderId`, via the `PRIMARY KEY CLUSTERED`
constraint) is part of the starting schema, not a stage — this experiment
is only about non-clustered covering behaviour.

## Why the before state needs a non-covering index, not no index at all

A Key Lookup only appears when a non-clustered index seeks successfully
but doesn't hold every column the query needs — it's the operator that
goes back to fetch what's missing. A bare heap/clustered-only table would
just produce a table/clustered-index scan for `CustomerId = 1234`, with no
seek and no lookup to eliminate. So `stage1-before` deliberately creates
`IX_Orders_CustomerId` on `CustomerId` alone first, confirms the query's
actual plan contains a Key Lookup, and only then adds `INCLUDE` columns.

## The query under test

`sql/03_query.sql` — filters on `CustomerId` (the index's key) and selects
`OrderId, OrderDate, Amount, Status`. `OrderId` needs no lookup (it's the
clustering key, always carried as every non-clustered index's row
locator); `OrderDate`, `Amount` and `Status` are not in the before-state
index, so each matching row costs a Key Lookup.

## The DROP_EXISTING choice

`sql/11_covering_index.sql` rebuilds the *same* index name
(`IX_Orders_CustomerId`) via `CREATE NONCLUSTERED INDEX ... WITH
(DROP_EXISTING = ON)` rather than creating a second, differently-named
index. That mirrors real practice, keeps the before/after comparison
about one index rather than two, and removes any doubt about which index
the optimizer chose in the after-state plan.

## Captured output

`output/stage1-before/query_stats_profile.txt` and
`output/stage2-after/query_stats_profile.txt` — `SET STATISTICS
IO/TIME/PROFILE` text, including the "logical reads" line and the actual
per-operator row counts.

`output/stage1-before/query_plan.sqlplan` and
`output/stage2-after/query_plan.sqlplan` — the actual execution plan as
XML (`SET STATISTICS XML ON`), extracted to just the `<ShowPlanXML>`
document. These are genuine *actual* plans (contain
`RunTimeCountersPerThread`/`ActualRows`), not estimated-only plans.

## Re-running

```
./scripts/run-experiment.sh
```

Idempotent: always removes and recreates the `day8-covering-sql`
container on host port **1434** (not 1433, so it never collides with
Task 1's `day8-sql` container if that's still running), regenerates the
database/table/data, and regenerates every capture in `output/`. Stops
and removes its own container when it finishes.

## Resolved ambiguities

1. **Engine.** Key Lookup, `INCLUDE`, actual execution plans and `SET
   STATISTICS IO ON` are SQL Server T-SQL concepts with no SQLite
   equivalent, so this runs SQL Server 2022 Developer edition in Docker
   under `--platform linux/amd64`, relying on Docker Desktop's Rosetta
   emulation on Apple Silicon — unsupported by Microsoft but a widely used
   dev configuration. Logical reads are a function of the query plan and
   page layout, not host CPU architecture, so the measurements are
   genuine.
2. **Scope versus Task 1.** Task 1 covered clustered vs non-clustered
   indexes across four staged measurements; Task 2 is narrower and
   self-contained: one query, one non-covering index producing a Key
   Lookup, then that lookup eliminated by `INCLUDE`. A different database
   (`CoveringLab`) is used so the two experiments cannot interfere.
3. **Schema.** The task names no table; `CoveringLab.dbo.Orders` was
   defined as described above.
4. **What "before" means.** A Key Lookup requires a seekable but
   non-covering index, not a bare table — see above.
5. **How the index becomes covering.** `DROP_EXISTING = ON` on the same
   index name, as described above.
6. **"Prove it from the plan."** Captured both as readable text (`SET
   STATISTICS PROFILE ON`) and as XML (`SET STATISTICS XML ON`, saved as
   `.sqlplan`), both carrying real runtime counters — no estimated-only
   plans were used anywhere. The proof is the presence, then absence, of
   the Key Lookup operator in the real captured plans.

## Password handling

`scripts/run-experiment.sh` generates its own random SA password at
runtime (`openssl rand`), holds it only in a shell variable for the
lifetime of that script run, and never prints, logs, or persists it
anywhere.
