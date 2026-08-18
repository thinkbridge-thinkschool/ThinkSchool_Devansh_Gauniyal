## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-8/task-2/day-8/task-2

## Notes for mentor

**Setup.** Database `CoveringLab`, table `dbo.Orders` (~100,000 deterministic synthetic rows, `CustomerId` cycling over ~5,000 values). The one query under test filters `CustomerId = 1234`, which matches **20 rows** — confirmed via `SELECT COUNT(*) ... WHERE CustomerId = 1234` in `sql/02_generate_data.sql`'s captured output. All numbers below are copied verbatim from the real captured files under `output/`, produced by an actual run of `scripts/run-experiment.sh` against SQL Server 2022 in Docker.

**BEFORE state — `stage1-before`.** Index: `IX_Orders_CustomerId` on `CustomerId`, no `INCLUDE`.

```sql
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId);
```

Query: `SELECT OrderId, OrderDate, Amount, Status FROM dbo.Orders WHERE CustomerId = 1234;`

Real captured plan (`output/stage1-before/query_stats_profile.txt`), operator by operator:

```
Nested Loops(Inner Join, OUTER REFERENCES:([CoveringLab].[dbo].[Orders].[OrderId]))
  |--Index Seek(OBJECT:([CoveringLab].[dbo].[Orders].[IX_Orders_CustomerId]))
  |--Clustered Index Seek(OBJECT:([CoveringLab].[dbo].[Orders].[PK__Orders__...]))
```

That `Clustered Index Seek` driven by `Nested Loops` off the index seek **is** the Key Lookup — one execution per matched row (`ActualExecutions="20"` in the captured XML), fetching `OrderDate`/`Amount`/`Status` because the index doesn't hold them. The XML plan (`output/stage1-before/query_plan.sqlplan`) carries this as an `IndexScan` element with `Lookup="1"` nested inside that `RelOp`.

Logical reads: **62** (`Table 'Orders'. Scan count 1, logical reads 62, ...`).

**The index with INCLUDE, rebuilt via DROP_EXISTING:**

```sql
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId)
    INCLUDE (OrderDate, Amount, Status)
    WITH (DROP_EXISTING = ON);
```

**AFTER state — `stage2-after`.** Same query, real captured plan (`output/stage2-after/query_stats_profile.txt`):

```
Index Seek(OBJECT:([CoveringLab].[dbo].[Orders].[IX_Orders_CustomerId]))
```

No `Nested Loops`, no `Clustered Index Seek`, no `Lookup="1"` anywhere in `output/stage2-after/query_plan.sqlplan` — the lookup is gone, replaced by a single covered `Index Seek` that reads `CustomerId`, `OrderDate`, `Amount` and `Status` directly from the index leaf.

Logical reads: **3** (`Table 'Orders'. Scan count 1, logical reads 3, ...`).

**Logical-reads delta:** 62 → 3, a drop of **59 logical reads (95%)** for the 20 rows this query matches.

**Why INCLUDE columns can't be seeked on.** `INCLUDE` columns are stored only at the index's leaf level, not in the upper B-tree pages used to navigate a seek — they ride along as payload on the leaf row, not as part of the sort order. That's exactly why they're read but never part of the seek predicate: the engine still seeks purely on the key column (`CustomerId`), then simply reads the extra columns off the same leaf page it already had to visit, instead of taking a separate trip to the clustered index.

**Resolved interpretations** (recorded so a mentor can challenge them):

1. **Engine.** Key Lookup, `INCLUDE`, and actual execution plans are SQL Server T-SQL concepts with no SQLite equivalent, so this runs SQL Server 2022 Developer edition in Docker under `--platform linux/amd64`, relying on Docker Desktop's Rosetta emulation on Apple Silicon — unsupported by Microsoft but a widely used dev configuration. Logical reads are a function of the query plan and page layout, not host CPU architecture, so the measurements are genuine.
2. **Scope versus Task 1.** Task 1 covered clustered vs non-clustered indexes across four stages; Task 2 is a separate, narrower experiment — one query, one non-covering index, then covered — in its own `CoveringLab` database so the two cannot interfere.
3. **Schema.** The task names no table; `CoveringLab.dbo.Orders` was defined with a surrogate clustered key, a customer integer, date/status/amount columns, and a padding column, as above.
4. **What "before" means.** A Key Lookup needs a seekable-but-non-covering index, not a bare table, so `stage1-before` builds `IX_Orders_CustomerId` first and confirms the lookup is genuinely present before adding `INCLUDE`.
5. **DROP_EXISTING.** The same index name is rebuilt in place rather than creating a second index, keeping the comparison about one index and removing any doubt about which index the optimizer chose.
6. **"Prove it from the plan."** Captured as both readable text (`SET STATISTICS PROFILE ON`) and XML (`SET STATISTICS XML ON`), both with real runtime counters — no estimated-only plans anywhere.

## What did you learn this session?

Watching the exact same `Clustered Index Seek` node disappear from the plan once `INCLUDE` covered the query made the Key Lookup concept concrete rather than theoretical.

## What would break this?

One more selected column outside the `INCLUDE` list brings the lookup straight back, and the 62-vs-3 win shrinks fast as the matched-row count drops toward one, where fixed overhead dominates.
