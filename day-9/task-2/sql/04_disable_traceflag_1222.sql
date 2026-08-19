-- Day 9 / Task 2 — disable trace flag 1222 and confirm it is off.
--
-- DBCC TRACESTATUS after TRACEOFF is the confirmation step. Called with a
-- specific flag number (rather than -1, which lists only the flags
-- currently on), it always returns exactly one row for that flag — the
-- Status column is 0 once it is off, not an empty result set.

DBCC TRACEOFF (1222, -1);
GO

DBCC TRACESTATUS (1222);
GO
