# Day 8 Task 1 — Clustered vs non-clustered indexes

Measures the read/write trade-off of adding a clustered index and two
non-clustered indexes to a ~100,000-row SQL Server table, using
`SET STATISTICS IO/TIME/PROFILE/XML` to capture real before/after evidence
rather than estimated plans.

## Schema

Database `IndexLab`, single table `dbo.Orders`:

| Column      | Type          | Notes                                             |
|-------------|---------------|----------------------------------------------------|
| OrderId     | INT IDENTITY  | surrogate key                                       |
| OrderDate   | DATETIME2(0)  | spread across a 5-year window                       |
| CustomerId  | INT           | ~5,000 distinct values (realistic cardinality)      |
| Status      | VARCHAR(20)   | skewed: `Completed` ~70%, `Disputed` ~1%            |
| Amount      | DECIMAL(10,2) | synthetic amount                                    |
| Description | CHAR(300)     | fixed-width padding so page density is predictable  |

All ~100,000 rows are generated deterministically from row-number
arithmetic (no `NEWID()`/`RAND()`), so re-running the load produces
byte-identical data every time.

## Staging methodology

The same three queries (`sql/03_queries.sql`) run at every stage with
`SET STATISTICS IO/TIME/PROFILE/XML ON`, so each index has a genuine
measured before and after:

| Stage                 | What exists                                    |
|------------------------|------------------------------------------------|
| `stage0-heap`          | table as a heap, no indexes at all              |
| `stage1-clustered`     | + clustered index on `OrderDate`                |
| `stage2-nc-customer`   | + non-clustered index on `CustomerId` (no INCLUDE) |
| `stage3-nc-covering`   | + non-clustered covering index on `CustomerId` INCLUDE (`Amount`, `Status`) |

Each query is designed to target exactly one index's transition:

- **Q1** (date range) — targets the clustered index. Compare
  `stage0-heap` → `stage1-clustered`.
- **Q2** (`CustomerId` equality, selects `Description`) — targets the
  plain non-clustered index. `Description` is deliberately never in any
  index's key/INCLUDE list, so Q2 always needs a Key Lookup; the
  interesting transition is `stage1-clustered` → `stage2-nc-customer`,
  where the Key Lookup first appears (replacing a full scan).
- **Q3** (`CustomerId` equality, selects only `CustomerId`/`Amount`/`Status`)
  — targets the covering index. Compare `stage2-nc-customer` →
  `stage3-nc-covering`, where the Key Lookup disappears entirely.

Write-side cost is measured by inserting 10,000 further synthetic rows
twice: once with only the clustered index in place
(`writecost-clustered-only`), once with all three indexes in place
(`writecost-all-indexes`). Each run cleans up its own inserted rows
afterward so the table stays at ~100,000 rows and repeat runs are
comparable.

## Captured output

`output/<stage>/q<N>_stats_profile.txt` — `SET STATISTICS IO/TIME/PROFILE`
text, including the "logical reads" line(s) and the actual per-operator
row counts.

`output/<stage>/q<N>_plan.sqlplan` — the actual execution plan as XML
(`SET STATISTICS XML ON`), extracted to just the `<ShowPlanXML>` document.
This is a genuine *actual* plan (contains `RunTimeCountersPerThread` /
`ActualRows`), not an estimated-only plan.

`output/writecost-*/insert_stats.txt` — IO/TIME stats for the 10,000-row
insert at each of the two write-cost stages.

## Re-running

```
./scripts/run-experiment.sh
```

Idempotent and safe to re-run from a clean state: it always removes and
recreates the `day8-sql` container (see caveat below), re-creates the
database/table, reloads the ~100k rows deterministically, and regenerates
every capture in `output/`.

## Resolved ambiguities

1. **Engine.** `SET STATISTICS IO ON` and the clustered/non-clustered
   distinction are SQL Server T-SQL and don't exist in SQLite, so the
   Day 7 SQLite approach can't satisfy this task. SQL Server images are
   amd64-only, so this runs SQL Server 2022 Developer edition in Docker
   under `--platform linux/amd64`, relying on Docker Desktop's Rosetta
   emulation on Apple Silicon. This isn't an officially supported
   configuration, but it's a widely used development setup, and logical
   read counts are a function of the query plan and page layout, not the
   host CPU architecture — the measurements are genuine.
2. **Schema.** The task names no table, so this experiment defines
   `IndexLab.dbo.Orders` with a surrogate key, a date column, a
   customer-id integer, a status column, a decimal amount, and a padding
   column, as described above.
3. **"Before/after each index."** A staged measurement across four
   stages (heap → clustered → +non-clustered → +covering), with the same
   three queries run at every stage, as described above.
4. **"The actual execution plan."** No GUI is required; the actual plan
   is captured both as readable text (`SET STATISTICS PROFILE ON`, with
   real row counts per operator) and as XML (`SET STATISTICS XML ON`,
   saved as `.sqlplan`). Estimated-only plans were not used anywhere.
5. **Write-side cost.** Measured as a 10,000-row insert under
   `SET STATISTICS IO/TIME ON`, once with only the clustered index and
   once with all three indexes, comparing the logical-write/CPU delta
   attributable to maintaining the two non-clustered indexes.

## Password handling

`scripts/run-experiment.sh` generates its own random SA password at
runtime (`openssl rand`), holds it only in a shell variable for the
lifetime of that script run, and never prints, logs, or persists it
anywhere. Because SQL Server bakes the SA password into `master` at first
boot, the script always removes and recreates the `day8-sql` container on
each run rather than "reusing" a stale one from an earlier run whose
password is no longer known — that's what keeps this rerunnable without
manual intervention.
