-- THE GRADED QUERY.
-- One row per quote (not one row per author -- that was Day 7 Task 1's graded query). Per
-- author: quote text, its timestamp, a running count of quotes up to and including this
-- one, the previous quote's CreatedAt via LAG, and the gap since it in days (raw + rounded).
--
-- The window is declared ONCE via the named WINDOW clause below and reused by every
-- column that needs it, rather than repeating the same OVER (...) spec on each one.
--
-- Id is included in the window's ORDER BY as the deterministic tie-break: Talia Marsh's
-- Quotes 6 and 7 share an identical CreatedAt, and without Id here their relative order
-- (and so which one "comes after" the other for LAG/running-count purposes) would be
-- arbitrary. With it, Quote 6 always precedes Quote 7, on every run, on every SQLite build.
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt,
    COUNT(*) OVER AuthorWindow AS RunningQuoteCount,
    LAG(q.CreatedAt) OVER AuthorWindow AS PreviousQuoteCreatedAt,
    -- SQLite has no DATEDIFF; T-SQL would write DATEDIFF(day, PreviousQuoteCreatedAt, CreatedAt).
    -- The first quote per author has no previous row, so LAG returns NULL and this
    -- subtraction propagates NULL rather than becoming 0 -- see the comment below on why
    -- that distinction matters.
    julianday(q.CreatedAt) - julianday(LAG(q.CreatedAt) OVER AuthorWindow) AS GapDaysRaw,
    -- The NULL-vs-0 distinction must survive rounding too: a first quote should report "no
    -- previous quote to compare to" (NULL), not "zero days since the last one" (0) -- a
    -- report that silently coerced this to 0 would claim every author's opening quote was
    -- itself a same-day repeat. ROUND(NULL) is itself NULL in SQLite, so this falls out for
    -- free rather than needing a special case.
    ROUND(
        julianday(q.CreatedAt) - julianday(LAG(q.CreatedAt) OVER AuthorWindow)
    , 0) AS GapDaysRounded
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
WINDOW AuthorWindow AS (
    PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id
    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
)
ORDER BY a.Name, q.CreatedAt, q.Id;
