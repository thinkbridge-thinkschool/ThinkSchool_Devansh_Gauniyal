-- Day 9 / Task 1 — drop the IsolationLab database. Idempotent.

USE master;
GO

IF DB_ID(N'IsolationLab') IS NOT NULL
BEGIN
    ALTER DATABASE IsolationLab SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE IsolationLab;
END
GO
