-- Creates the IndexLab database used for the whole experiment. Idempotent:
-- safe to run against a fresh container or one that already has it.
IF DB_ID(N'IndexLab') IS NULL
BEGIN
    CREATE DATABASE IndexLab;
END;
GO

ALTER DATABASE IndexLab SET RECOVERY SIMPLE;
GO
