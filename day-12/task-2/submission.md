## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-12/task-2/day-12/task-2

## Notes for mentor

### Both implementations

`DapperComparison/EfQueries.cs` (`AsNoTracking` projection variant):

```csharp
public static List<QuoteWallItem> RunProjection(QuotesDbContext context, DateTime submittedSinceUtc)
{
    return context.Quotes
        .AsNoTracking()
        .Where(q => q.CreatedAt >= submittedSinceUtc)
        .OrderByDescending(q => q.CreatedAt)
        .ThenByDescending(q => q.Id)
        .Select(q => new QuoteWallItem
        {
            QuoteId = q.Id,
            QuoteText = q.Text,
            AuthorName = q.Author!.Name,
            AuthorCountry = q.Author!.Country,
            SubmittedOn = q.CreatedAt.ToString("yyyy-MM-dd")
        })
        .ToList();
}
```

`DapperComparison/DapperQueries.cs`:

```csharp
public const string Sql = @"
SELECT q.Id AS QuoteId, q.Text AS QuoteText, a.Name AS AuthorName, a.Country AS AuthorCountry, q.CreatedAt AS CreatedAt
FROM Quotes q
INNER JOIN Authors a ON q.AuthorId = a.Id
WHERE q.CreatedAt >= @SubmittedSinceUtc
ORDER BY q.CreatedAt DESC, q.Id DESC";

public static List<QuoteWallItem> Run(string dataSource, DateTime submittedSinceUtc)
{
    using var connection = new SqliteConnection($"Data Source={dataSource}");
    connection.Open();

    var rows = connection.Query<QuoteWallRow>(Sql, new { SubmittedSinceUtc = submittedSinceUtc });

    return rows.Select(r => new QuoteWallItem
    {
        QuoteId = r.QuoteId,
        QuoteText = r.QuoteText,
        AuthorName = r.AuthorName,
        AuthorCountry = r.AuthorCountry,
        SubmittedOn = r.CreatedAt.ToString("yyyy-MM-dd")
    }).ToList();
}
```

### The SQL

EF's real logged SQL for the projection variant (`output/ef-projection-sql.log`):

```sql
SELECT "q"."Id", "q"."Text", "a"."Name", "a"."Country", "q"."CreatedAt"
FROM "Quotes" AS "q"
INNER JOIN "Authors" AS "a" ON "q"."AuthorId" = "a"."Id"
WHERE "q"."CreatedAt" >= @submittedSinceUtc
ORDER BY "q"."CreatedAt" DESC, "q"."Id" DESC
```

The Dapper SQL as executed (`output/dapper-sql.log`):

```sql
SELECT q.Id AS QuoteId, q.Text AS QuoteText, a.Name AS AuthorName, a.Country AS AuthorCountry, q.CreatedAt AS CreatedAt
FROM Quotes q
INNER JOIN Authors a ON q.AuthorId = a.Id
WHERE q.CreatedAt >= @SubmittedSinceUtc
ORDER BY q.CreatedAt DESC, q.Id DESC
```

Same columns, same join, same filter, same order — EF's generated SQL and the hand-written Dapper SQL are materially the same query.

### The timing comparison

| Variant | Median ms | Median allocated bytes | Bytes/row | Row count |
|---|---|---|---|---|
| EF tracked (**unfair baseline**) | 48.22 | 15,786,496 | 1,578.65 | 10,000 |
| EF `AsNoTracking` projection | 10.65 | 5,671,656 | 567.17 | 10,000 |
| Dapper | 11.26 | 4,793,360 | 479.34 | 10,000 |

The fair result: projection versus Dapper are within noise on elapsed time (Dapper 0.62ms slower on the median), with Dapper allocating about 15.5% less per row.
Measured against tracked EF instead, Dapper looks roughly 4.28x faster — a gap that is almost entirely change-tracking overhead, not Dapper versus EF.

### The rule

Drop to Dapper only after you've measured the real bottleneck against a fair EF baseline, not against a tracked query — on this measurement, `AsNoTracking().Select(...)` already closed nearly all of the gap, and the remaining difference was small enough to be noise on timing and a modest 15% on allocation, so for most read paths projection alone is the fix, and reaching for Dapper on top of it only pays for itself at genuine hot-path scale (high enough request volume that a per-row allocation difference actually compounds) or when the query is something LINQ can't express cleanly; either way you're accepting hand-written SQL that no longer refactors with the model, no compile-time check if a column is renamed, and no change tracking, so the query has to be worth losing those for specifically, not as a general reflex.

## Interpretations

- Query chosen: Day 12 Task 1's quote-wall read — highest-volume read path, already had an optimized EF form to compare against.
- Fair-comparison requirement: measured EF tracked, EF `AsNoTracking` projection, and Dapper — not just two — because tracked EF is the wrong baseline and the gap against it is not the real Dapper-versus-EF answer.
- Stopwatch rather than BenchmarkDotNet: BenchmarkDotNet wasn't a named or unavoidable addition, so `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()` measured instead, with the hygiene rules applied by hand.
- The two "What this builds" tags read as topic labels for the day, not deliverables — no MediatR and no `Span<T>`/memory-pooling code was written.
- All logged parameter values are synthetic — the only parameter is a fixed `2000-01-01` cutoff constant, never real data.
- The Dapper SQL is parameterized (`@SubmittedSinceUtc`, bound via Dapper) — never string interpolation or concatenation.

## What did you learn this session?

The fair EF baseline closed almost the entire gap I expected Dapper to win outright - the honest number was noise on timing and a modest allocation edge, not the dramatic win the unfair comparison suggested. Measuring against tracked EF would have made a much smaller, real effect look far larger than it is.

## What would break this?

The Dapper query doesn't refactor with the model, so a renamed column breaks it at runtime instead of at compile time the way the LINQ side would catch immediately. This measurement also came from one laptop against a 10,000-row SQLite file in a single process - a network-bound server database at a different scale could show a different gap entirely.
