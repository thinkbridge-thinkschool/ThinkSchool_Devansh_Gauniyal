-- Day 9 / Task 1 — Phantom read, SESSION B (the reader).
--
-- Global interleaving order (Session A's steps are in
-- 12_phantom_sessionA.sql):
--   1. Session B STEP 1 (this file)  — set the isolation level under test
--   2. Session B STEP 2 (this file)  — begin a transaction, run the range
--                                       query WHERE Balance BETWEEN 2000.00
--                                       AND 3000.00 (first read — matches
--                                       Accounts 3, 4, 5)
--   3. Session A STEP 1              — INSERTs Account 11 (Balance 2750.00,
--                                       inside the range) and commits —
--                                       under SERIALIZABLE this blocks until
--                                       this session commits (STEP 4) or
--                                       times out with error 1222
--   4. Session B STEP 3 (this file)  — re-run the SAME range query, same
--                                       transaction (second read)
--   5. Session B STEP 4 (this file)  — commit
--
-- __ISOLATION_LEVEL__ is substituted at run time: REPEATABLE READ for the
-- occurring-anomaly run (REPEATABLE READ locks only the rows already read,
-- not the gaps between them, so STEP 3 sees A's new row — a phantom, 3 rows
-- become 4), SERIALIZABLE for the preventing run (A blocks and times out,
-- so STEP 3 still returns the original 3 rows). That single token is the
-- only difference between the two runs of this file.

-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL __ISOLATION_LEVEL__;
GO

-- STEP 2: begin a transaction and run the range query (first read).
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.Accounts WHERE Balance BETWEEN 2000.00 AND 3000.00 ORDER BY Id;
GO

-- STEP 3: re-run the same range query, inside the same transaction (second
-- read).
SELECT Id, Balance FROM dbo.Accounts WHERE Balance BETWEEN 2000.00 AND 3000.00 ORDER BY Id;
GO

-- STEP 4: commit, releasing any range lock this transaction is holding.
COMMIT TRANSACTION;
GO
