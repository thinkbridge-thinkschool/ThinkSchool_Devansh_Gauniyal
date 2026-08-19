-- Day 9 / Task 2 — capture route (b): the trace flag 1222 deadlock report
-- text out of the SQL Server error log.
--
-- Trace flag 1222 (enabled by 03_enable_traceflag_1222.sql) makes SQL
-- Server write the deadlock graph across many consecutive error-log lines,
-- one element or attribute group per line, indented to mirror the XML
-- structure that the Extended Events route captures directly - it is NOT
-- angle-bracket XML text, just the same information flattened into plain
-- log lines (e.g. "deadlock-list", "  process id=... waitresource=...",
-- "    keylock hobtid=... objectname=..."). Reading the whole current log
-- (rather than filtering by a search string) is deliberate: the individual
-- continuation lines of that block do not all contain a literal match for
-- a narrower filter, and xp_readerrorlog's search string matches per-line,
-- not across the whole multi-line block. The orchestrator extracts the
-- deadlock-list block from this output with a text-processing pass over
-- these lines (see scripts/run-experiment.sh).
--
-- Run with sqlcmd's -y 0 (no output-width truncation) or long XML lines
-- will be cut off at sqlcmd's default 256-character display width.

EXEC sys.xp_readerrorlog 0, 1;
GO
