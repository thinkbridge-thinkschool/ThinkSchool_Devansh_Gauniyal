-- Running total via SUM() OVER, showing the default RANGE frame vs an explicit
-- ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW frame side by side, using Talia
-- Marsh's tied Quotes 6 and 7 (identical CreatedAt) to expose the difference in real output.
--
-- RunningCount_DefaultRange has NO frame clause, so SQLite falls back to the implicit
-- default: RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW. RANGE groups every row that
-- ties on the ORDER BY value (CreatedAt here) into one peer group and gives ALL peers the
-- SAME total -- the total as of the end of that whole group, not a per-row increment. So
-- both of Talia's tied quotes show the SAME running count (both counted together at once),
-- instead of incrementing 1 then 2.
--
-- RunningCount_ExplicitRows uses ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW, which
-- counts strictly by physical row position -- Id breaks the CreatedAt tie in its ORDER BY
-- -- giving a true 1, 2, 3... increment even when CreatedAt values are identical.
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt,
    SUM(1) OVER (PARTITION BY q.AuthorId ORDER BY q.CreatedAt) AS RunningCount_DefaultRange,
    SUM(1) OVER (
        PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningCount_ExplicitRows
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
ORDER BY a.Name, q.CreatedAt, q.Id;
