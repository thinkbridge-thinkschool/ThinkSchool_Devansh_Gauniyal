-- Day 9 / Task 2 — enable trace flag 1222.
--
-- Trace flag 1222 makes SQL Server write a full deadlock graph, as XML, to
-- the SQL Server error log every time the deadlock monitor resolves a
-- deadlock. The -1 applies it server-wide (not just to this connection),
-- which is required here since the deadlock repro runs on two separate
-- connections, neither of which is this one.
--
-- Trace flag 1222 is unrelated to error 1222 ("Lock request time out period
-- exceeded") despite sharing the number — see README.md for the
-- disambiguation. This script is one of two independent capture routes used
-- in this experiment (the other is the system_health Extended Events ring
-- buffer, which needs no trace flag at all).

DBCC TRACEON (1222, -1);
GO

DBCC TRACESTATUS (1222);
GO
