-- Answers: "which author/tag combinations exist at all, versus which have never actually
-- been used together" -- e.g. every author paired with 'unused-tag' (Id 6), which no quote
-- ever carries.
-- Row count of the raw CROSS JOIN is exactly (COUNT Authors) * (COUNT Tags): a cross join has
-- no ON condition to filter the product down, so it is the full author/tag grid before the
-- LEFT JOIN below narrows down which pairs were ever actually used.
SELECT
    a.Name AS AuthorName,
    t.Name AS TagName,
    CASE WHEN used.TagId IS NULL THEN 0 ELSE 1 END AS EverUsedByAuthor
FROM Authors a
CROSS JOIN Tags t
LEFT JOIN (
    SELECT DISTINCT q.AuthorId, qt.TagId
    FROM Quotes q
    INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
) used ON used.AuthorId = a.Id AND used.TagId = t.Id
ORDER BY a.Name, t.Name;
