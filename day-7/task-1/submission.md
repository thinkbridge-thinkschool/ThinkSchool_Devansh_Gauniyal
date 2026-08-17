## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-7/task-1/day-7/task-1

## Notes for mentor

```sql
-- THE GRADED QUERY.
-- One statement, built entirely from non-recursive CTEs -- no correlated subquery in the
-- SELECT list. Returns every author (including the zero-quote author) with: name, quote
-- count (0, not NULL, when they have none), and the text + timestamp of their most-recent
-- quote.
WITH QuoteCounts AS (
    -- Per-author aggregate. Only authors with at least one quote get a row here;
    -- the final LEFT JOIN + COALESCE below is what turns "no row" into "0".
    SELECT
        AuthorId,
        COUNT(*) AS QuoteCount
    FROM Quotes
    GROUP BY AuthorId
),
RankedQuotes AS (
    -- Picks each author's single most-recent quote via ROW_NUMBER() instead of a
    -- correlated subquery. CreatedAt DESC orders newest-first; Id DESC is the
    -- deterministic tie-break for authors whose two most-recent quotes share an
    -- identical CreatedAt (Marcus Aurelius, Quotes 9 and 10, both '2023-08-01T09:00:00'
    -- in this seed data) -- without it, which quote counts as "most recent" would be
    -- arbitrary and could change from run to run.
    SELECT
        AuthorId,
        Text,
        CreatedAt,
        ROW_NUMBER() OVER (PARTITION BY AuthorId ORDER BY CreatedAt DESC, Id DESC) AS rn
    FROM Quotes
)
SELECT
    a.Id AS AuthorId,
    a.Name AS AuthorName,
    COALESCE(qc.QuoteCount, 0) AS QuoteCount,
    rq.Text AS MostRecentQuoteText,
    rq.CreatedAt AS MostRecentQuoteCreatedAt
FROM Authors a
LEFT JOIN QuoteCounts qc ON qc.AuthorId = a.Id
LEFT JOIN RankedQuotes rq ON rq.AuthorId = a.Id AND rq.rn = 1
-- Stable ordering: by name first (the natural reading order for this report), Id as a
-- final tie-break in case two authors ever share a name.
ORDER BY a.Name, a.Id;
```

TOP 10 ROWS
```
AuthorId  AuthorName           QuoteCount  MostRecentQuoteText                                                          MostRecentQuoteCreatedAt
--------  -------------------  ----------  ---------------------------------------------------------------------------  ------------------------
6         Chrysippus           2           The wise man is free from passion.                                           2023-07-04T11:00:00
7         Confucius            0
2         Epictetus            3           First say to yourself what you would be; and then do what you have to do.    2023-07-30T10:00:00
9         Friedrich Nietzsche  3           Without music, life would be a mistake.                                      2023-11-02T10:00:00
8         Laozi                3           When I let go of what I am, I become what I might be.                        2023-09-25T17:00:00
3         Marcus Aurelius      4           Very little is needed to make a happy life.                                  2023-08-01T09:00:00
4         Ryan Holiday         2           Focus on what is in your control, let go of what is not.                     2023-10-01T13:30:00
1         Seneca               3           It is not that we have a short time to live, but that we waste a lot of it.  2023-06-05T14:20:00
10        Simone de Beauvoir   3           It is up to each of us to invent our own path.                               2023-12-01T11:00:00
5         Zeno of Citium       2           Man conquers the world by conquering himself.                                2023-06-19T15:00:00
```

Why a CTE here rather than a correlated subquery: a CTE computes each author's most-recent quote once via a single `ROW_NUMBER()` pass over `Quotes`, while a correlated subquery in the `SELECT` list would re-scan `Quotes` once per author row and still couldn't return the text, timestamp, and deterministic tie-break together without correlating twice.

Engine used: SQLite, the same engine as Day 5's own Quotes DB, and the only option here since SQL Server container images are x86_64-only and won't run natively on this arm64 Mac. Join demonstrations are in `sql/10_inner_join.sql`, `sql/11_left_join.sql`, and `sql/12_cross_join.sql`; the recursive CTE over the author influence chain is in `sql/21_recursive_cte_influence_chain.sql`.

## What did you learn this session?
I learned that `ROW_NUMBER() OVER (PARTITION BY ...)` combined with an explicit `Id` tie-break in the `ORDER BY` replaces a correlated subquery cleanly for picking one row per group. I also learned SQLite requires `WITH RECURSIVE` for the whole `WITH` clause even when only one of several CTEs inside it is actually recursive.

## What would break this?
A cycle in `InfluencedByAuthorId` would make the recursive CTE loop forever without the depth cap I added, which I verified by seeding a synthetic two-author cycle in a test and watching it still terminate. Storing `CreatedAt` in anything other than strict ISO-8601 text would break the lexicographic ordering the whole most-recent-quote logic depends on.
