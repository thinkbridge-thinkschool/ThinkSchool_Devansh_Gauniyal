-- Day 9 / Task 2 — capture route (a): the default system_health Extended
-- Events session's ring buffer target.
--
-- SQL Server runs the system_health session out of the box and it records
-- every deadlock the server detects, whether or not trace flag 1222 is
-- enabled. This is one of two independent capture routes used here (see
-- 31_capture_deadlock_errorlog.sql for the other) — each is a fallback for
-- the other, so having both is stronger evidence than either alone.
--
-- Run with sqlcmd's -y 0 (no output-width truncation) or the XML will be
-- cut off at sqlcmd's default 256-character display width.
--
-- QUOTED_IDENTIFIER must be explicitly ON for this connection: the XML
-- data type methods used below (.value(), .query(), .nodes()) are in the
-- same category of feature (alongside indexed views, indexes on computed
-- columns, and filtered indexes) that SQL Server refuses to run under the
-- wrong SET options, raising error 1934 otherwise.
SET QUOTED_IDENTIFIER ON;
GO

SELECT CAST(target_data AS XML) AS TargetData
INTO #ring_buffer
FROM sys.dm_xe_session_targets st
JOIN sys.dm_xe_sessions s ON s.address = st.event_session_address
WHERE s.name = N'system_health'
  AND st.target_name = N'ring_buffer';

-- .query('.') returns the whole <event> element (timestamp attribute plus
-- the nested <data name="xml_report"><value>...deadlock report...</value>)
-- rather than guessing at the exact nesting of the deadlock report inside
-- it — that nesting has changed across SQL Server versions, but the report
-- itself is always in there. The orchestrator extracts just the
-- <deadlock...>...</deadlock...> portion when it saves the .xdl file.
SELECT
    event_xml.value('(@timestamp)[1]', 'datetime2') AS EventTimestamp,
    event_xml.query('.') AS DeadlockReportXml
INTO #deadlock_events
FROM #ring_buffer
CROSS APPLY TargetData.nodes('RingBufferTarget/event[@name="xml_deadlock_report"]') AS T(event_xml);

-- Only the XML is selected here (EventTimestamp is used purely to order
-- by, not returned) so sqlcmd's captured output is the bare XML document,
-- with nothing else on its line, and can be saved as-is to a .xdl file.
SELECT TOP (1)
    DeadlockReportXml
FROM #deadlock_events
ORDER BY EventTimestamp DESC;

DROP TABLE #deadlock_events;
DROP TABLE #ring_buffer;
GO
