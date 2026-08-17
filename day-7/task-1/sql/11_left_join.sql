-- Answers: "every author, including ones who have never been quoted."
-- COUNT(*) vs COUNT(q.Id): for Confucius (zero quotes) the LEFT JOIN still produces exactly
-- one output row, with every q.* column NULL. COUNT(*) counts that row -- it exists -- giving
-- 1, while COUNT(q.Id) counts only non-NULL values of q.Id, giving the true count of 0. Using
-- COUNT(*) here would silently misreport Confucius as having one quote.
SELECT
    a.Id AS AuthorId,
    a.Name AS AuthorName,
    COUNT(*) AS RowCount_IncludesNullRow,
    COUNT(q.Id) AS QuoteCount_TrueCount
FROM Authors a
LEFT JOIN Quotes q ON q.AuthorId = a.Id
GROUP BY a.Id, a.Name
ORDER BY a.Name;
