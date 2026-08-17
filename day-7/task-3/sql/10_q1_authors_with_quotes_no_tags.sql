-- Business question: "Which authors have quotes, but none of those quotes carry a tag?"
--
-- Reading: an author qualifies only if they have at least one quote AND zero tags across
-- ALL of their quotes. An author with even one tagged quote (Otis Bramwell, who has 2
-- tagged and 2 untagged quotes) does NOT qualify -- he does have some tags. This is the
-- only reading under which "no tags" says something about the author as a whole rather
-- than about one quote in isolation.
--
-- EXCEPT over NOT EXISTS / LEFT JOIN ... IS NULL: EXCEPT reads as exactly the English
-- sentence -- "authors with quotes, minus authors with a tagged quote" -- as one set
-- subtraction with no correlation and no risk of picking the wrong join column and
-- accidentally changing the row grain. (sql/20_operator_contrasts.sql shows the
-- LEFT JOIN ... IS NULL equivalent side by side, and why it needs a GROUP BY/HAVING to
-- match this same "no tags at all" answer.)
SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id

EXCEPT

SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id

ORDER BY AuthorName;
