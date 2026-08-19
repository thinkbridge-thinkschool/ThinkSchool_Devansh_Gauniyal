-- Day 9 / Task 1 — create the IsolationLab database if it does not already exist.
-- Idempotent and independently runnable.

IF DB_ID(N'IsolationLab') IS NULL
BEGIN
    CREATE DATABASE IsolationLab;
END
GO
