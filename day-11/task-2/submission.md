## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-11/task-2/day-11/task-2

## Notes for mentor

**Where the fix lives:** a fresh, self-contained project (`day-11/task-2/FastApi`) that
reproduces task-1's model, seed (50 authors, 5,000 quotes) and endpoint contract exactly.
Task-1's committed baseline was read-only throughout - it's the "before" evidence this
comparison depends on, never overwritten.

**Before / After p99**

| | Before | After - projection (primary) | After - split query |
|---|---|---|---|
| p50 | 42.24ms | 0.458ms | 18.17ms |
| p99 | **215.81ms** | **2.55ms** | **34.52ms** |
| Improvement | - | **~84.6×** | **~6.25×** |
| Throughput | 1.54MB/s | 129.65MB/s | 4.24MB/s |

Before: `day-11/task-1/output/load-test.txt` (quoted, not re-run). After:
`output/load-test-projection.txt` / `output/load-test-split.txt`. Same tool (bombardier
2.0.2), concurrency (20), duration (10s), seed, route and warmup procedure as task-1 -
`run-profile.sh` verifies this live against task-1's file before running anything, and
fails loudly on a mismatch.

Projection clears the 10× target by ~8×. Split query does not (6.25×): its second query
still joins and materializes all 5,000 quote rows just to count them, while projection
counts inside the database via a correlated subquery. Both are real fixes over the N+1
baseline; they aren't equally cheap for a count-only access pattern.

**The changes**

- **Index** - EF Core's convention was simply left in place, no explicit `HasIndex` call.
  Confirmed via `output/schema-dump.txt`: `IX_Quotes_AuthorId` exists.
- **Projection** (primary) - `Select(a => new AuthorWithQuotesDto(a.Id, a.Name, a.Country, a.Quotes.Count()))`.
  **1 query** total (confirmed from `sql-sample-projection.log`); never touches the change
  tracker, the same tracking cost Day 10 Task 1 measured directly.
- **Split query** - `Include(a => a.Quotes).AsSplitQuery()`. **2 fixed queries**, not
  1+N (confirmed from `sql-sample-split.log`); avoids the cartesian explosion a plain
  `Include()` would cause (50 × 100 = 5,000 duplicated rows in one JOIN).

**Before / After plans**

Before (`day-11/task-1/output/query-plan.txt`): `SCAN q` - full table scan on the
per-author query. After, both fixed variants (`output/query-plan-projection.txt`,
`output/query-plan-split.txt`): `SEARCH q USING INDEX IX_Quotes_AuthorId` - an index
lookup, not a scan. These are SQLite `EXPLAIN QUERY PLAN` outputs, not SQL Server actual
plans like Day 8 - no cost or row estimates, just `SCAN`/`SEARCH` and the index name.

## What did you learn this session?

Split query still moves every quote row over the wire even after fixing N+1, while
projection asks the database to count instead - that gap is why only one clears 10×.

## What would break this?

Projection's one-query win only holds while the DTO needs a count, not individual quotes.
These figures also came from a laptop where bombardier shared CPU with the API itself.
