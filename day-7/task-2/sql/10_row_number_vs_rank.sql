-- ROW_NUMBER, RANK, and DENSE_RANK computed side by side over the same partition/order,
-- against Talia Marsh's Quotes 6 and 7, which share an identical CreatedAt (a genuine tie).
--
-- On that tie (both rows tie for the first position in author order):
--   ROW_NUMBER  -- always assigns distinct 1, 2, 3... even on a tie. This query's ORDER BY
--                  has no further tie-break, so which of the two tied rows gets 1 and which
--                  gets 2 is not guaranteed stable -- see 20_author_quote_windows.sql, whose
--                  window ORDER BY adds Id specifically to make that choice deterministic.
--   RANK        -- gives both tied rows the SAME rank (1 and 1), then SKIPS the next rank --
--                  the third row jumps straight to rank 3, leaving a gap where the tie was.
--   DENSE_RANK  -- also gives both tied rows the same rank (1 and 1), but does NOT skip --
--                  the third row gets rank 2, with no gap.
--
-- Choose ROW_NUMBER when exactly one row per group is needed (e.g. "pick the latest"),
-- RANK when gaps should reflect how many rows tied for a position (e.g. competition
-- standings, where two joint-firsts push third place down to rank 3), and DENSE_RANK for
-- consecutive tier numbers with no gaps (e.g. "top 3 distinct price tiers").
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt,
    ROW_NUMBER() OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt) AS RowNum,
    RANK()       OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt) AS Rnk,
    DENSE_RANK() OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt) AS DenseRnk
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
ORDER BY a.Name, q.CreatedAt, q.Id;
