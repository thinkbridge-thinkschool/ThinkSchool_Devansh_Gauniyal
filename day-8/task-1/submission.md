## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-8/task-1/day-8/task-1

## Notes for mentor

**Setup.** Database `IndexLab`, table `dbo.Orders` (~100,000 deterministic
synthetic rows: `CustomerId` cycles over ~5,000 values, `Status` is skewed
70% `Completed` / 1% `Disputed`, `Description` is a fixed CHAR(300) so page
counts are predictable). Four stages: heap → + clustered index on
`OrderDate` → + non-clustered index on `CustomerId` → + covering
non-clustered index on `CustomerId` INCLUDE (`Amount`, `Status`). All
numbers below are copied verbatim from `output/**/q*_stats_profile.txt`
and `output/**/q*_plan.sqlplan`, produced by an actual run of
`scripts/run-experiment.sh` against SQL Server 2022 in Docker.

**Index DDL**

```sql
-- Clustered
CREATE CLUSTERED INDEX CIX_Orders_OrderDate
    ON dbo.Orders (OrderDate);

-- Non-clustered, no INCLUDE (demonstrates the Key Lookup cost)
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId);

-- Non-clustered, covering (eliminates the Key Lookup for Q3)
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Covering
    ON dbo.Orders (CustomerId)
    INCLUDE (Amount, Status);
```

**Queries and before/after logical reads**

| Query | Targets | Why this index helps | Before | After | Stage transition |
|---|---|---|---|---|---|
| Q1 — date range (`OrderDate BETWEEN ...`) | Clustered index | Rows become physically ordered by `OrderDate`, turning a full scan into a contiguous range read | 4,348 | 76 | stage0-heap → stage1-clustered |
| Q2 — `CustomerId = 1234`, selects `Description` (never indexed) | Plain non-clustered index | Seeks the index on `CustomerId` but still needs a Key Lookup back into the clustered index for `Description` | 4,375 | 62 | stage1-clustered → stage2-nc-customer |
| Q3 — `CustomerId = 1234`, selects only `CustomerId`/`Amount`/`Status` | Covering non-clustered index | Key + INCLUDE columns fully satisfy the query, so the Key Lookup disappears entirely | 62 | 3 | stage2-nc-customer → stage3-nc-covering |

**Actual-plan operator shift** (from the captured `.sqlplan` XML, all with
real `RunTimeCountersPerThread`/`ActualRows`, not estimates):

- Q1: `Table Scan` (stage0-heap) → `Clustered Index Seek` (stage1-clustered).
- Q2: `Clustered Index Scan` (stage1-clustered) → `Index Seek` on
  `IX_Orders_CustomerId` joined via `Nested Loops` to a `Clustered Index
  Seek` carrying `Lookup="1"` (stage2-nc-customer) — that `Lookup="1"`
  attribute is SQL Server's own marker for a genuine Key Lookup.
- Q3: the same `Index Seek` + `Nested Loops` + `Clustered Index Seek
  (Lookup="1")` pattern at stage2-nc-customer, collapsing to a single
  `Index Seek` with no lookup at all once the covering index exists
  (stage3-nc-covering).

**Write-side cost.** Inserting the same 10,000 synthetic rows cost 43,266
logical reads / 66 ms CPU with only the clustered index in place
(`writecost-clustered-only`), versus 121,612 logical reads (101,174 against
`Orders` plus 20,438 against a worktable) / 97 ms CPU once both
non-clustered indexes also had to be maintained (`writecost-all-indexes`)
— roughly 2.8x the logical I/O and 47% more CPU to write the same rows.

**Resolved interpretations** (recorded so a mentor can challenge them):

1. **Engine.** SQLite (used in Day 7) has no `SET STATISTICS IO` or
   clustered/non-clustered distinction, so this runs SQL Server 2022
   Developer edition in Docker under `--platform linux/amd64`, relying on
   Docker Desktop's Rosetta emulation on Apple Silicon — an unsupported
   but widely used dev configuration. Logical reads are a function of the
   query plan and page layout, not host CPU architecture, so the
   measurements are genuine.
2. **Schema.** The task names no table; `IndexLab.dbo.Orders` was defined
   as described above, with a padding column so page density is
   meaningful rather than noise.
3. **"Before/after each index."** A four-stage progression (heap →
   clustered → +non-clustered → +covering), with the same three queries
   run at every stage.
4. **"The actual execution plan."** Captured both as readable text
   (`SET STATISTICS PROFILE ON`) and as XML (`SET STATISTICS XML ON`,
   saved as `.sqlplan`), both carrying real runtime counters — no
   estimated-only plans were used anywhere.
5. **Write-side cost.** Measured as a 10,000-row insert under
   `SET STATISTICS IO/TIME ON`, once with only the clustered index and
   once with all three, as above.

## What did you learn this session?

Adding one index dropped Q1's logical reads by two orders of magnitude, and the plan XML's `Lookup="1"` attribute is what finally made the Key Lookup concept concrete.

## What would break this?

Q3's covering index only works because it selects exactly `CustomerId`/`Amount`/`Status` — one more output column brings the Key Lookup straight back, and the write-cost gap would widen further at a bigger table or with more indexes.
