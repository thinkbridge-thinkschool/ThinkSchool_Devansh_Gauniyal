-- Day 9 / Task 2 — create the DeadlockLab database if it does not already
-- exist. Idempotent and independently runnable.

IF DB_ID(N'DeadlockLab') IS NULL
BEGIN
    CREATE DATABASE DeadlockLab;
END
GO
