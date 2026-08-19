-- Day 9 / Task 1 — Dirty read, SESSION A (the writer).
--
-- Global interleaving order (Session B's steps are in
-- 10_dirty_read_sessionB.sql):
--   1. Session A STEP 1 (this file)  — begin a transaction, update Account 2's
--                                       balance, leave it UNCOMMITTED
--   2. Session B STEP 1              — set the isolation level under test
--   3. Session B STEP 2              — read Account 2 while A's update is
--                                       still uncommitted (the dirty-read
--                                       attempt)
--   4. Session A STEP 2 (this file)  — roll back, discarding the update
--   5. Session B STEP 3              — read Account 2 again, after A's
--                                       rollback
--
-- This file is byte-identical between the occurring-anomaly run and the
-- preventing run: only Session B's isolation level changes (see that file).
-- SET LOCK_TIMEOUT lives in the same batch as the statement it guards
-- (rather than a batch of its own): a lock-timeout error in a later,
-- separate batch was found to abort that batch outright, silently
-- dropping any statement after it — including the marker this orchestrator
-- relies on to know the step finished.

-- STEP 1: begin a transaction and update Account 2's balance, do not commit.
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
UPDATE dbo.Accounts SET Balance = 9999.99 WHERE Id = 2;
GO

-- STEP 2: roll back — the update above must never be committed.
ROLLBACK TRANSACTION;
GO
