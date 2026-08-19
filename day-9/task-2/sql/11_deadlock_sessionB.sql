-- Day 9 / Task 2 — Deadlock repro, SESSION B (BROKEN lock ordering).
--
-- Global interleaving order (Session A's steps are in
-- 10_deadlock_sessionA.sql; see scripts/run-experiment.sh for how the
-- orchestrator forces this exact interleaving using named pipes):
--   1. Session A STEP 1              — begin a transaction, locks Accounts
--                                       row Id=1, does not commit
--   2. Session B STEP 1 (this file)  — begin a transaction, lock Orders
--                                       row Id=1, do not commit
--   3. Session A STEP 2              — requests Orders row Id=1, which this
--                                       session already holds — blocks
--   4. Session B STEP 2 (this file)  — request Accounts row Id=1, which
--                                       Session A already holds — blocks
--   5. Deadlock monitor              — steps 3 and 4 form a circular wait
--                                       (A waits on B, B waits on A); SQL
--                                       Server picks one session as the
--                                       victim (error 1205) and rolls it
--                                       back, letting the other proceed
--   6. Both sessions (this file's    — the survivor commits; the victim's
--      STEP 3 and A's STEP 3)          COMMIT errors harmlessly since its
--                                       transaction was already rolled back
--
-- BROKEN ORDERING: this session locks Orders before Accounts — the REVERSE
-- of Session A, which locks Accounts before Orders. That reversal is what
-- makes a circular wait possible; see 21_fixed_sessionB.sql for the fix,
-- which changes only the order of the two UPDATE statements below — nothing
-- else in this file differs from that one.
--
-- Deliberately NO SET LOCK_TIMEOUT here: a lock timeout would fire before
-- SQL Server's own deadlock monitor runs and would be captured as error
-- 1222 (a timeout), not a genuine deadlock (error 1205). The sessions must
-- be left to genuinely wait so the deadlock monitor is the one that acts.

BEGIN TRANSACTION;

-- STEP 1: lock Orders row Id=1.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-B' WHERE Id = 1;
GO

-- STEP 2: request Accounts row Id=1 — Session A holds this lock at this
-- point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
GO

-- STEP 3: commit if this session survived. If this session was chosen as
-- the deadlock victim, SQL Server already rolled its transaction back, and
-- this COMMIT errors ("no corresponding BEGIN TRANSACTION") — that harmless
-- secondary error is itself part of the captured evidence, not a bug.
COMMIT TRANSACTION;
GO
