-- Day 9 / Task 1 — record the real snapshot-isolation settings for IsolationLab.
--
-- If READ_COMMITTED_SNAPSHOT were ON, READ COMMITTED would use row versioning
-- instead of shared locks and the non-repeatable-read demonstration would
-- behave differently (no blocking, a versioned read instead). This script
-- must be run and its output recorded before any anomaly is demonstrated.
-- Both flags are expected to read OFF; if either is unexpectedly ON, stop
-- and report it rather than proceeding.

SELECT
    name AS DatabaseName,
    is_read_committed_snapshot_on AS ReadCommittedSnapshotOn,
    snapshot_isolation_state_desc AS AllowSnapshotIsolationState
FROM sys.databases
WHERE name = N'IsolationLab';
GO
