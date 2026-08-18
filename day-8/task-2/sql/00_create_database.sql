-- Creates the CoveringLab database used for this experiment. Idempotent.
-- Deliberately a DIFFERENT database from Task 1's IndexLab so the two
-- experiments cannot interfere with each other.
IF DB_ID(N'CoveringLab') IS NULL
BEGIN
    CREATE DATABASE CoveringLab;
END;
GO

ALTER DATABASE CoveringLab SET RECOVERY SIMPLE;
GO
