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
