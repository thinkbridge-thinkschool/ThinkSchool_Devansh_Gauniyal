-- Day 9 / Task 1 — Dirty read, SESSION B (the reader).
--
-- Global interleaving order (Session A's steps are in
-- 10_dirty_read_sessionA.sql):
--   1. Session A STEP 1              — begins a transaction, updates Account
--                                       2's balance to 9999.99, does not
--                                       commit
--   2. Session B STEP 1 (this file)  — set the isolation level under test
--   3. Session B STEP 2 (this file)  — read Account 2 while A's update is
--                                       still uncommitted (the dirty-read
--                                       attempt)
--   4. Session A STEP 2              — rolls back
--   5. Session B STEP 3 (this file)  — read Account 2 again, after A's
--                                       rollback
--
-- READ UNCOMMITTED is substituted at run time: READ UNCOMMITTED for the
-- occurring-anomaly run (STEP 2 sees the dirty value), READ COMMITTED for
-- the preventing run (STEP 2 blocks on A's exclusive lock until A's
-- rollback, then returns the original value — a consistent, non-dirty
-- read). That single token is the only difference between the two runs of
-- this file.

-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

-- STEP 2: read while Session A's update is uncommitted (the dirty-read
-- attempt).
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 2;
GO

-- STEP 3: read again after Session A has rolled back.
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 2;
GO
