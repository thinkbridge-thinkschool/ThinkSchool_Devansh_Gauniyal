-- Teaching file: several small, independently-runnable statements contrasting set
-- operators against equivalent joins/comparisons, over the same seed data.
--
-- General rules that apply to every set operator below: the two sides must select the
-- same NUMBER of columns, in types that are compatible (SQLite is dynamically typed and
-- lenient here, but the column COUNT must match exactly); the combined result dedupes on
-- WHOLE-ROW equality, not on any single column; and EXCEPT/INTERSECT treat two NULLs as
-- equal for matching purposes, whereas an ordinary '=' comparison between two NULLs
-- evaluates to NULL (neither true nor false) under SQL's three-valued logic -- see the
-- last section below for a concrete demonstration. Oracle does not have EXCEPT at all;
-- it spells the same operator MINUS.

-- ============================================================================
-- 1) UNION vs UNION ALL row counts (same data as sql/12_q3_combined_distinct_tags.sql)
-- ============================================================================
SELECT 'UNION' AS SetOperator, COUNT(*) AS RowCount FROM (
    SELECT Name FROM Tags WHERE Category = 'classic'
    UNION
    SELECT Name FROM Tags WHERE Category = 'modern'
);

SELECT 'UNION ALL' AS SetOperator, COUNT(*) AS RowCount FROM (
    SELECT Name FROM Tags WHERE Category = 'classic'
    UNION ALL
    SELECT Name FROM Tags WHERE Category = 'modern'
);
-- UNION ALL counts every row from both sides (6 classic + 6 modern = 12); UNION collapses
-- the one duplicate tag name ('wisdom', seeded in both categories) down to 11.

-- ============================================================================
-- 2) EXCEPT vs LEFT JOIN ... IS NULL, same business question as sql/10
-- ============================================================================
SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
EXCEPT
SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
ORDER BY AuthorName;

-- The LEFT JOIN equivalent needs GROUP BY + HAVING, not just "WHERE qt.TagId IS NULL": a
-- plain WHERE would also surface Otis Bramwell, because SOME of his quote rows have no
-- matching QuoteTags row -- but he has other quotes that DO. Only GROUP BY author with
-- HAVING COUNT(qt.TagId) = 0 correctly requires that NONE of an author's quotes are
-- tagged, matching EXCEPT's "no tags at all" answer above exactly.
SELECT a.Name AS AuthorName
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
LEFT JOIN QuoteTags qt ON qt.QuoteId = q.Id
GROUP BY a.Id, a.Name
HAVING COUNT(qt.TagId) = 0
ORDER BY AuthorName;

-- ============================================================================
-- 3) INTERSECT vs INNER JOIN, same business question as sql/11 -- where the JOIN
--    version duplicates rows that INTERSECT cannot.
-- ============================================================================
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
WHERE t.Category = 'modern';

-- A naive INNER JOIN attempt at the same question -- joining each author's classic-tagged
-- quote rows to their modern-tagged quote rows on author -- DUPLICATES: an author with 2
-- classic-tagged quotes and 3 modern-tagged quotes produces 2 * 3 = 6 joined rows for one
-- author, not 1. INTERSECT cannot do this because it operates on already-distinct rows
-- from each side, never multiplying anything together.
WITH ClassicTagged AS (
    SELECT a.Id AS AuthorId, a.Name AS AuthorName
    FROM Authors a
    INNER JOIN Quotes q ON q.AuthorId = a.Id
    INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
    INNER JOIN Tags t ON t.Id = qt.TagId
    WHERE t.Category = 'classic'
),
ModernTagged AS (
    SELECT a.Id AS AuthorId, a.Name AS AuthorName
    FROM Authors a
    INNER JOIN Quotes q ON q.AuthorId = a.Id
    INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
    INNER JOIN Tags t ON t.Id = qt.TagId
    WHERE t.Category = 'modern'
)
SELECT c.AuthorName, COUNT(*) AS DuplicatedJoinRowCount
FROM ClassicTagged c
INNER JOIN ModernTagged m ON m.AuthorId = c.AuthorId
GROUP BY c.AuthorName;

-- ============================================================================
-- 4) NULLs: EXCEPT/INTERSECT treat two NULLs as a match; plain '=' does not.
-- ============================================================================
SELECT 1 AS Id, NULL AS Note
EXCEPT
SELECT 1 AS Id, NULL AS Note;
-- Returns 0 rows: EXCEPT treated the two (1, NULL) rows as equal and subtracted the match out.

SELECT 1 AS Id, NULL AS Note
INTERSECT
SELECT 1 AS Id, NULL AS Note;
-- Returns 1 row: INTERSECT treated the two (1, NULL) rows as equal and kept the match.

SELECT (NULL = NULL) AS WhatPlainEqualsReturnsForNullVsNull;
-- Returns NULL, not 1/true: a plain '=' comparison between two NULLs is neither true nor
-- false under SQL's three-valued logic, so a naive WHERE a.Note = b.Note would silently
-- drop this pairing instead of matching it the way EXCEPT/INTERSECT just did above.
