-- Day 9 / Task 2 — Deadlock FIX, SESSION B (consistent lock ordering).
--
-- The ONLY difference from 11_deadlock_sessionB.sql is the ORDER of the two
-- UPDATE statements below: Accounts is now locked before Orders, matching
-- Session A's order. No hint, isolation-level change, retry loop, or
-- DEADLOCK_PRIORITY was added — the same two statements simply swap
-- position, which is the entire fix (see README.md for why consistent
-- ordering prevents the cycle).
--
-- Global interleaving order in the fixed run (Session A's steps are in
-- 20_fixed_sessionA.sql):
--   1. Session A STEP 1             — begin a transaction, locks Accounts
--                                      row Id=1, does not commit
--   2. Session B STEP 1 (this file) — begin a transaction, request Accounts
--                                      row Id=1 — Session A already holds
--                                      it, so this blocks (ordinary lock
--                                      contention, not a deadlock)
--   3. Session A STEP 2             — locks Orders row Id=1 — nothing else
--                                      holds it yet, so it succeeds
--                                      immediately
--   4. Session A STEP 3             — commits, releasing both locks
--   5. Session B STEP 1 (this file, — this session's blocked request
--      cont.)                         unblocks and completes
--   6. Session B STEP 2 (this file) — request Orders row Id=1 — Session A
--                                      already committed and released it,
--                                      so this succeeds immediately
--   7. Session B STEP 3 (this file) — commit
-- No cycle can form: both sessions now agree on the order in which they
-- acquire Accounts and Orders, so neither can ever hold a later resource
-- while waiting on an earlier one.

BEGIN TRANSACTION;

-- STEP 1: request Accounts row Id=1 — this blocks until Session A commits.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
GO

-- STEP 2: request Orders row Id=1 — Session A has already released it by
-- the time this session gets here, so this succeeds immediately.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-B' WHERE Id = 1;
GO

-- STEP 3: commit.
COMMIT TRANSACTION;
GO
