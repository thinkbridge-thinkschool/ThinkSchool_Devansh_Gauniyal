-- Day 9 / Task 1 — Non-repeatable read, SESSION B (the reader).
--
-- Global interleaving order (Session A's steps are in
-- 11_nonrepeatable_sessionA.sql):
--   1. Session B STEP 1 (this file)  — set the isolation level under test
--   2. Session B STEP 2 (this file)  — begin a transaction, read Account 1
--                                       (first read)
--   3. Session A STEP 1              — updates Account 1's balance to
--                                       1111.11 and commits — under
--                                       REPEATABLE READ this blocks until
--                                       this session commits (STEP 4) or
--                                       times out with error 1222
--   4. Session B STEP 3 (this file)  — read Account 1 again, same
--                                       transaction (second read)
--   5. Session B STEP 4 (this file)  — commit
--
-- __ISOLATION_LEVEL__ is substituted at run time: READ COMMITTED for the
-- occurring-anomaly run (STEP 3 sees A's committed update — the two reads
-- differ), REPEATABLE READ for the preventing run (A blocks and times out,
-- so STEP 3 still sees the original value — the two reads match). That
-- single token is the only difference between the two runs of this file.

-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL __ISOLATION_LEVEL__;
GO

-- STEP 2: begin a transaction and read Account 1 (first read).
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 1;
GO

-- STEP 3: read Account 1 again, inside the same transaction (second read).
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 1;
GO

-- STEP 4: commit, releasing any lock this transaction is holding.
COMMIT TRANSACTION;
GO
