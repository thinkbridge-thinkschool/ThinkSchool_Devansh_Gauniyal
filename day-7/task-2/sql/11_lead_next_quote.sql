-- LEAD looks forward to each quote's FOLLOWING quote -- the mirror image of LAG, which
-- looks backward to the previous one.
-- The last quote per author has no following row within its partition to look ahead to,
-- so LEAD returns NULL there -- there is nothing after it, not "zero days until next".
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt,
    LEAD(q.Text) OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id) AS NextQuoteText,
    LEAD(q.CreatedAt) OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id) AS NextQuoteCreatedAt,
    ROUND(
        julianday(LEAD(q.CreatedAt) OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id))
        - julianday(q.CreatedAt)
    , 0) AS DaysUntilNextQuote
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
ORDER BY a.Name, q.CreatedAt, q.Id;
