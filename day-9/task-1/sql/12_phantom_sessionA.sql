-- Day 9 / Task 1 — Phantom read, SESSION A (the writer).
--
-- Global interleaving order (Session B's steps are in
-- 12_phantom_sessionB.sql):
--   1. Session B STEP 1              — set the isolation level under test
--   2. Session B STEP 2              — begin a transaction, run the range
--                                       query (first read)
--   3. Session A STEP 1 (this file)  — INSERT a new row that falls inside
--                                       the range and (auto-commit) commit —
--                                       under SERIALIZABLE this blocks on
--                                       B's key-range lock until B commits
--   4. Session B STEP 3              — re-run the SAME range query, same
--                                       transaction (second read)
--   5. Session B STEP 4              — commit, releasing the range lock
--                                       (only matters if A is still waiting)
--
-- The phantom is produced by an INSERT, not an UPDATE — that distinction is
-- the point of this anomaly (a row lock cannot protect a range that does
-- not exist yet). This file is byte-identical between the occurring-anomaly
-- run and the preventing run: only Session B's isolation level changes (see
-- that file). SET LOCK_TIMEOUT lives in the same batch as the INSERT it
-- guards (rather than a batch of its own): a lock-timeout error in a later,
-- separate batch was found to abort that batch outright, silently dropping
-- any statement after it — including the marker this orchestrator relies on
-- to know the step finished.

-- STEP 1: insert a new account whose balance (2750.00) falls inside the
-- range Session B is querying (2000.00 to 3000.00). No explicit transaction
-- — the statement auto-commits on success. Under SERIALIZABLE this blocks
-- until Session B's transaction ends (commit) or this statement's own
-- LOCK_TIMEOUT expires first (error 1222).
SET LOCK_TIMEOUT 5000;
INSERT INTO dbo.Accounts (Id, AccountName, Balance, Category)
VALUES (11, N'Account 0011', 2750.00, 'Retail');
GO
