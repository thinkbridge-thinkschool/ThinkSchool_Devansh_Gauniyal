-- Business question: "Which authors have at least one quote tagged 'classic' AND at least
-- one quote tagged 'modern'?" -- per the documented schema interpretation: an author is
-- "in" a category if any one of their quotes carries a tag of that category.
--
-- INTERSECT over a self-join / two EXISTS clauses: INTERSECT states the intent directly --
-- "authors in the classic set, and also in the modern set" -- as two independently
-- readable queries combined by one operator, rather than a self-join whose join condition
-- exists purely to re-merge two logically separate memberships back into one row, or two
-- correlated EXISTS subqueries repeating the same join path twice.
--
-- SQLite has no INTERSECT ALL. That does not matter here -- author names are already
-- distinct per author in this seed -- but it means "how many overlapping ROWS" (as
-- opposed to "which distinct authors overlap") cannot be asked with INTERSECT alone; it
-- would need to be emulated with a ROW_NUMBER()-based approach instead.
SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
INNER JOIN Tags t ON t.Id = qt.TagId
WHERE t.Category = 'classic'

INTERSECT

SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
INNER JOIN Tags t ON t.Id = qt.TagId
WHERE t.Category = 'modern'

ORDER BY AuthorName;
