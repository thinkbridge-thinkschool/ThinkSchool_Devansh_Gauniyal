-- Answers: "every quote together with the name of the author who wrote it."
-- Silently drops: authors with zero quotes (Confucius, Id 7) never appear in this result --
-- there is no Quotes row for them to join to, so an INNER JOIN just omits them, with no
-- error or warning that anything was left out.
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
ORDER BY a.Name, q.CreatedAt, q.Id;
