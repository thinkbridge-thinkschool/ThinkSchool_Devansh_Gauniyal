-- Day 9 / Task 2 — Deadlock FIX, SESSION A (consistent lock ordering).
--
-- Session A's resource order was never the problem — it already locked
-- Accounts before Orders in the broken run. The fix in this experiment is
-- entirely in Session B (see 21_fixed_sessionB.sql): making B agree with
-- A's order, Accounts before Orders, removes the circular wait. So the
-- statements below are identical to 10_deadlock_sessionA.sql — same
-- transaction, same two UPDATEs, same order, no LOCK_TIMEOUT, no isolation
-- level change, no retry loop, no DEADLOCK_PRIORITY. Only this comment
-- block differs, to describe the run this file is now part of.
--
-- Global interleaving order in the fixed run (Session B's steps are in
-- 21_fixed_sessionB.sql):
--   1. Session A STEP 1 (this file) — begin a transaction, lock Accounts
--                                      row Id=1, do not commit
--   2. Session B STEP 1             — begin a transaction, request Accounts
--                                      row Id=1 — since this session (A)
--                                      already holds it, B blocks (ordinary
--                                      lock contention, not a deadlock)
--   3. Session A STEP 2 (this file) — lock Orders row Id=1 — nothing else
--                                      holds it yet, so this succeeds
--                                      immediately
--   4. Session A STEP 3 (this file) — commit, releasing both locks
--   5. Session B STEP 1 (cont.)     — B's blocked request unblocks and
--                                      completes
--   6. Session B STEP 2             — request Orders row Id=1 — A already
--                                      committed and released it, so this
--                                      succeeds immediately
--   7. Session B STEP 3             — commit
-- No cycle can form: both sessions now agree on the order in which they
-- acquire Accounts and Orders, so neither can ever hold a later resource
-- while waiting on an earlier one.

BEGIN TRANSACTION;

-- STEP 1: lock Accounts row Id=1.
UPDATE dbo.Accounts SET Balance = Balance + 100.00 WHERE Id = 1;
GO

-- STEP 2: lock Orders row Id=1. Nothing else holds it at this point in the
-- fixed interleaving, so this returns immediately.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-A' WHERE Id = 1;
GO

-- STEP 3: commit, releasing both locks.
COMMIT TRANSACTION;
GO
