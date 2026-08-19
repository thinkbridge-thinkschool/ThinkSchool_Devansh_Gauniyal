-- Day 9 / Task 2 — drop the DeadlockLab database. Idempotent.

USE master;
GO

IF DB_ID(N'DeadlockLab') IS NOT NULL
BEGIN
    ALTER DATABASE DeadlockLab SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE DeadlockLab;
END
GO
