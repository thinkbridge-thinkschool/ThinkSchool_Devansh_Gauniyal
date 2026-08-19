-- Day 9 / Task 2 — schema for the deadlock experiment.
--
-- "Two-resource" is the operative phrase in the exercise, so this uses two
-- separate tables rather than two rows in one table: dbo.Accounts and
-- dbo.Orders. Two distinct tables make the resource-acquisition order
-- explicit and readable in the repro scripts, which is the point of the
-- exercise — this is about lock ordering, not volume, so a handful of rows
-- is correct. All data is synthetic — no real names, emails, card or
-- account numbers. Idempotent and independently runnable: safe to re-run
-- against an existing DeadlockLab database.

USE DeadlockLab;
GO

IF OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL
    DROP TABLE dbo.Accounts;
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
    DROP TABLE dbo.Orders;
GO

CREATE TABLE dbo.Accounts
(
    Id          INT           NOT NULL PRIMARY KEY,
    AccountName NVARCHAR(50)  NOT NULL,
    Balance     DECIMAL(12,2) NOT NULL
);
GO

CREATE TABLE dbo.Orders
(
    Id               INT          NOT NULL PRIMARY KEY,
    OrderDescription NVARCHAR(50) NOT NULL,
    OrderStatus      VARCHAR(20)  NOT NULL
);
GO
