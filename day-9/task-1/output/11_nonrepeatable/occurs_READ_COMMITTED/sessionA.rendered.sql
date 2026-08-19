-- Day 9 / Task 1 — Non-repeatable read, SESSION A (the writer).
--
-- Global interleaving order (Session B's steps are in
-- 11_nonrepeatable_sessionB.sql):
--   1. Session B STEP 1              — set the isolation level under test
--   2. Session B STEP 2              — begin a transaction, read Account 1
--                                       (first read)
--   3. Session A STEP 1 (this file)  — update Account 1's balance and
--                                       (auto-commit) commit — under
--                                       REPEATABLE READ this blocks on B's
--                                       shared lock until B commits
--   4. Session B STEP 3              — read Account 1 again, same
--                                       transaction (second read)
--   5. Session B STEP 4              — commit, releasing the shared lock
--                                       (only matters if A is still waiting)
--
-- This file is byte-identical between the occurring-anomaly run and the
-- preventing run: only Session B's isolation level changes (see that file).
-- SET LOCK_TIMEOUT lives in the same batch as the UPDATE it guards (rather
-- than a batch of its own): a lock-timeout error in a later, separate batch
-- was found to abort that batch outright, silently dropping any statement
-- after it — including the marker this orchestrator relies on to know the
-- step finished.

-- STEP 1: update Account 1's balance. No explicit transaction — the
-- statement auto-commits on success. Under REPEATABLE READ this blocks
-- until Session B's transaction ends (commit) or this statement's own
-- LOCK_TIMEOUT expires first (error 1222).
SET LOCK_TIMEOUT 5000;
UPDATE dbo.Accounts SET Balance = 1111.11 WHERE Id = 1;
GO
