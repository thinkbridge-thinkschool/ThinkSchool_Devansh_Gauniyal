## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-7/task-3/day-7/task-3

## Notes for mentor

Schema interpretation: `Tags` carries a `Category` column restricted to `'classic'` or `'modern'`. An author is "in the classic set" if at least one of their quotes carries a tag whose category is `'classic'`, and likewise for `'modern'`.

**Question 1: Which authors have quotes, but none of those quotes carry a tag?**

```sql
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
```
```
AuthorName
-----------
Wilder Voss

Row count: 1
```
Operator used: **EXCEPT** — it beats `NOT EXISTS`/`LEFT JOIN ... IS NULL` because it states the set subtraction directly with no join column to get wrong.

**Question 2: Which authors have at least one quote tagged 'classic' AND at least one quote tagged 'modern'?**

```sql
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
```
```
AuthorName
----------
Anouk Fenn

Row count: 1
```
Operator used: **INTERSECT** — it beats a self-join/two `EXISTS` clauses because it merges two independently readable memberships into one answer without a join condition that exists purely to re-combine them.

**Question 3: What is the combined list of distinct tag names across the classic and modern categories?**

```sql
-- Business question: "What is the combined list of distinct tag names across the classic
-- and modern categories?"
--
-- UNION, not UNION ALL: the seed data deliberately has a tag named 'wisdom' as TWO
-- different Tag rows -- Id 4 (classic) and Id 7 (modern), same Name, different Category.
-- Selecting Name alone, UNION ALL would return 'wisdom' twice among 12 total rows (6
-- classic tags + 6 modern tags); UNION dedupes on the selected column and collapses that
-- one duplicate name down to a single row, giving 11 distinct names. See
-- results/20_operator_contrasts.txt for the actual counted rows of both.
SELECT Name AS TagName FROM Tags WHERE Category = 'classic'
UNION
SELECT Name AS TagName FROM Tags WHERE Category = 'modern'
ORDER BY TagName;
```
```
TagName
------------
agility
antiquity
asceticism
design
mindfulness
minimalism
productivity
rhetoric
stoicism
virtue
wisdom

Row count: 11
```
Operator used: **UNION** — `UNION ALL` would return 12 rows (counted for real in `results/20_operator_contrasts.txt`) because the seeded 'wisdom' tag exists once per category; `UNION` collapses it to the 11 actually-distinct names.

Engine: SQLite, chosen because SQL Server container images are x86_64-only and won't run natively on this arm64 Mac. SQLite has neither `INTERSECT ALL` nor `EXCEPT ALL` — only the plain, already-deduplicating forms of both operators exist.

## What did you learn this session?
I learned that "no tags" is ambiguous the moment an author's quotes are only partly tagged, and EXCEPT forced me to pick a reading (zero tags across all their quotes) rather than let a query silently guess. I also learned EXCEPT and INTERSECT compare whole rows, not just one column, so an extra selected column can silently change which rows count as matching.

## What would break this?
A tag with the same name seeded in both categories would silently collapse into one row under UNION if I only cared about names and forgot Category mattered for a different question. The "no tags" question is genuinely ambiguous for an author with some tagged and some untagged quotes — reading it as "not every quote is tagged" instead of "zero tags at all" would change who appears in that answer entirely.
