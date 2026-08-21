## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-11/task-2/day-11/task-2

## Notes for mentor

### Where the fix lives

"Now fix it" could be read as editing task-1 in place. That is not what this does: task-1
is committed, pushed, graded work, and its captured baseline (`day-11/task-1/output/`) is
the "before" evidence this task depends on - overwriting it would destroy the thing being
compared against. Instead, `day-11/task-2/FastApi` is a fresh, self-contained project that
reproduces task-1's model, seed and endpoint contract exactly (same `Author`/`Quote`
shape, same 50-author/5,000-quote deterministic seed, same response field set), then
applies the fixes there. Nothing under `day-11/task-1` or `day-3/task-3/QuotesApi` was
read for anything other than recovering the exact comparison parameters, and nothing in
either was modified.

### Resolved interpretations (all recorded so a mentor can challenge them)

1. **Where the fix lives.** Covered above - a fresh project in `day-11/task-2`, not an
   edit to task-1.
2. **Like-for-like comparison.** Every parameter below was recovered from task-1's
   committed `output/load-test.txt` and `output/environment.txt` and reused verbatim:
   tool (**bombardier 2.0.2 darwin/arm64**), concurrency (**20**), duration (**10s**),
   seed (**50 authors, 5,000 quotes**, same deterministic generator), endpoint route
   (`/api/authors/quote-summary`, identical relative path), and the same
   warmup-then-discard procedure (`bombardier -c 10 -d 3s`, discarded). `run-profile.sh`
   asserts the concurrency, duration and installed tool version against task-1's file
   *before* running anything, and fails loudly on any mismatch - confirmed by actually
   running it: see `output/load-test-projection.txt` and `output/environment.txt`.
3. **Honesty about the 10× target.** Reported below exactly as measured - one variant
   clears it by a wide margin, the other does not, and both are stated plainly. Nothing
   about the before figure was re-run under different load, and nothing about the after
   load (row counts, concurrency, duration, fields returned) was changed to flatter it.
4. **Which N+1 fix to use.** Both were implemented and measured, since the task names
   both and the comparison between them is itself informative. **Projection is nominated
   as the primary fixed endpoint** - it hits the theoretical minimum of one query for
   this access pattern and clears the 10× target by roughly 8×. Split query is measured
   too, as a genuine comparison, and it does not clear 10× here - explained below with
   the query-count and plan evidence, not asserted.
5. **The index.** Task-1 explicitly removed `ForeignKeyIndexConvention` to suppress the
   index EF Core creates by convention on a required FK. Task-2 does the opposite by
   *doing nothing*: `QuotesDbContext` does not override `ConfigureConventions` at all, so
   the convention runs normally and creates `IX_Quotes_AuthorId` without any explicit
   `HasIndex(...)` call. Confirmed against the real created schema in
   `IndexExistsTests`, not assumed from the model code - see `output/schema-dump.txt`.
6. **Before/after plans.** The before plan is quoted verbatim from
   `day-11/task-1/output/query-plan.txt`, not regenerated. The after plans are captured
   fresh from task-2's own two fixed queries into separate files, since their SQL differs.
   These are SQLite `EXPLAIN QUERY PLAN` outputs, not SQL Server actual execution plans as
   captured in Day 8 - no cost estimates, no row estimates, no operator tree, just
   `SCAN`/`SEARCH` and the index name if one was used.
7. **Evidence.** Every number below is copied verbatim from a file under `output/`
   (after) or `day-11/task-1/output/` (before), produced by real runs on 2026-08-21.
   Nothing here is invented.

### Before / After p99

| | Before (task-1) | After - projection (primary) | After - split query |
|---|---|---|---|
| p50 | 42.24ms | 0.458ms | 18.17ms |
| p99 | **215.81ms** | **2.55ms** | **34.52ms** |
| Improvement (p99) | - | **~84.6×** | **~6.25×** |
| Throughput | 1.54MB/s | 129.65MB/s | 4.24MB/s |
| Successful requests | 4,083 (10s) | 343,736 (10s) | 11,247 (10s) |

Source files: before from `day-11/task-1/output/load-test.txt` (2026-08-21 baseline run,
already committed, quoted not re-run); after from `output/load-test-projection.txt` and
`output/load-test-split.txt` (this run).

**Load parameters, confirmed identical between the two runs:**
- Tool: `bombardier version 2.0.2 darwin/arm64` - identical string in both `environment.txt` files.
- Concurrency: `-c 20` in both commands.
- Duration: `-d 10s` in both commands (not a request count).
- Seed: 50 authors, 5,000 quotes in both - confirmed by `ArtefactTests.Load_parameters_match_task1_field_by_field`, which parses "Authors returned: 50" from both `sql-sample.log` files and asserts equality.
- Route: `/api/authors/quote-summary` - the identical relative path in both.
- Warmup: `bombardier -c 10 -d 3s`, discarded, in both.
- Machine: the same Apple Silicon (arm64) laptop, API and load generator sharing CPU, in both.

**Honesty about the 10× target:** the projection fix clears it decisively - roughly
**84.6×**, about 8× past the target. The split-query fix does **not** clear it - roughly
**6.25×**, short of the 10× target. That is a real, reproducible finding, not a shortfall
in effort: split query still executes a second statement that does a full `INNER JOIN`
across all 5,000 quote rows and materializes every one of them into tracked-free `Quote`
objects just to count them per author, whereas the projection's single correlated
`COUNT(*)` subquery never materializes an individual quote row at all - the database
counts, and only 50 integers cross the wire. Both are real fixes over the N+1 baseline;
they are not equally cheap for an access pattern that only needs a count.

### The changes made

Original (task-1, unmodified, reproduced here as `Queries.RunSlow` for the in-process
tests - `day-3/task-3/QuotesApi/Performance/AuthorQuoteSummaryQuery.cs`):
```csharp
public static List<AuthorWithQuotesDto> RunSlow(QuotesDbContext context)
{
    var authors = context.Authors.OrderBy(a => a.Id).ToList(); // query 1
    var summaries = new List<AuthorWithQuotesDto>(authors.Count);
    foreach (var author in authors)
    {
        context.Entry(author).Collection(a => a.Quotes).Load(); // query 2..N+1
        summaries.Add(new AuthorWithQuotesDto(author.Id, author.Name, author.Country, author.Quotes.Count));
    }
    return summaries;
}
```

Fix 1 - projection (`FastApi/Queries.cs`):
```csharp
public static List<AuthorWithQuotesDto> RunProjection(QuotesDbContext context)
{
    return context.Authors
        .OrderBy(a => a.Id)
        .Select(a => new AuthorWithQuotesDto(a.Id, a.Name, a.Country, a.Quotes.Count()))
        .ToList();
}
```
Real captured query count for one request (`output/sql-sample-projection.log`): **1**.
`a.Quotes.Count()` inside the projection translates to a correlated scalar subquery, so
the whole thing is a single SQL statement - nothing is loaded as a tracked entity, since a
projection into a plain record is never added to the change tracker (the same tracking
cost Day 10 Task 1 measured directly).

Fix 2 - Include with split queries (`FastApi/Queries.cs`):
```csharp
public static List<AuthorWithQuotesDto> RunSplitQuery(QuotesDbContext context)
{
    var authors = context.Authors
        .AsNoTracking()
        .Include(a => a.Quotes)
        .AsSplitQuery()
        .OrderBy(a => a.Id)
        .ToList();

    return authors
        .Select(a => new AuthorWithQuotesDto(a.Id, a.Name, a.Country, a.Quotes.Count))
        .ToList();
}
```
Real captured query count for one request (`output/sql-sample-split.log`): **2** - fixed,
confirmed not to scale with author count by `QueryCountTests`. `AsSplitQuery()` avoids the
cartesian explosion a plain `Include()` would cause (a single JOIN repeats every author
row once per quote - 50 authors × 100 quotes = 5,000 duplicated rows just to describe 50
authors) by issuing two compact queries instead of one bloated one.

The index (`FastApi/QuotesDbContext.cs` - no explicit `HasIndex` call, the EF Core
convention was simply not suppressed):
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Quote>(entity =>
    {
        entity.HasOne(q => q.Author)
            .WithMany(a => a.Quotes)
            .HasForeignKey(q => q.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    });
}
```
Real schema dump (`output/schema-dump.txt`) confirms `IX_Quotes_AuthorId` exists.

### Before / After execution plans

Before (quoted from `day-11/task-1/output/query-plan.txt`, not regenerated):
```
SELECT "q"."Id", "q"."AuthorId", "q"."CreatedAt", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."AuthorId" = @p

Plan output (id | parent | notused | detail):
2 | 0 | 216 | SCAN q
```

After - projection (`output/query-plan-projection.txt`, captured fresh):
```
SELECT "a"."Id", "a"."Name", "a"."Country", (
          SELECT COUNT(*)
          FROM "Quotes" AS "q"
          WHERE "a"."Id" = "q"."AuthorId")
      FROM "Authors" AS "a"
      ORDER BY "a"."Id"

Plan output (id | parent | notused | detail):
3 | 0 | 216 | SCAN a
9 | 0 | 0 | CORRELATED SCALAR SUBQUERY 1
14 | 9 | 53 | SEARCH q USING COVERING INDEX IX_Quotes_AuthorId (AuthorId=?)
```
The line that changed: the quotes-side access goes from `SCAN q` (full table scan, task-1)
to `SEARCH q USING COVERING INDEX IX_Quotes_AuthorId` (index lookup, task-2). The `SCAN a`
line is the outer, unfiltered scan over all 50 authors - harmless and expected, unrelated
to the index fix.

After - split query (`output/query-plan-split.txt`, captured fresh, second statement
shown - the one that touches Quotes):
```
SELECT "q"."Id", "q"."AuthorId", "q"."CreatedAt", "q"."Text", "a"."Id"
      FROM "Authors" AS "a"
      INNER JOIN "Quotes" AS "q" ON "a"."Id" = "q"."AuthorId"
      ORDER BY "a"."Id"

Plan output (id | parent | notused | detail):
5 | 0 | 216 | SCAN a
7 | 0 | 61 | SEARCH q USING INDEX IX_Quotes_AuthorId (AuthorId=?)
```
Same proof: `SEARCH q USING INDEX IX_Quotes_AuthorId`, not a scan - but this plan still
joins and returns all 5,000 quote rows, which is the real cost this variant pays that the
projection's plan does not.

## What did you learn this session?

Split query still moves every quote row over the wire even after fixing N+1, while
projection asks the database to count instead - that gap is why only one clears 10×.

## What would break this?

Projection's one-query win only holds while the DTO needs a count, not individual quotes.
These figures also came from a laptop where bombardier shared CPU with the API itself.
