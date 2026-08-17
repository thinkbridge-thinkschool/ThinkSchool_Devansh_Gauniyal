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
