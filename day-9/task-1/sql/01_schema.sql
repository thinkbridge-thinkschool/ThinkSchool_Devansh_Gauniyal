-- Day 9 / Task 1 — schema for the isolation-level experiments.
--
-- One small table is enough: this task is about locking behaviour, not
-- volume. dbo.Accounts holds an integer id, a synthetic account name, a
-- decimal balance (used for the non-repeatable-read row and the phantom-read
-- range predicate), and a category column (an alternative range predicate).
-- All data is synthetic — no real names, emails, or account numbers.
-- Idempotent and independently runnable: safe to re-run against an existing
-- IsolationLab database.

USE IsolationLab;
GO

IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    DROP TABLE dbo.Accounts;
GO

CREATE TABLE dbo.Accounts
(
    Id          INT           NOT NULL PRIMARY KEY,
    AccountName NVARCHAR(50)  NOT NULL,
    Balance     DECIMAL(12,2) NOT NULL,
    Category    VARCHAR(20)   NOT NULL
);
GO
