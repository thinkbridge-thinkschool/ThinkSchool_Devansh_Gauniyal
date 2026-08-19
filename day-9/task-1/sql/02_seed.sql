-- Day 9 / Task 1 — seed data for dbo.Accounts.
--
-- Small, obviously-synthetic rows only (names like 'Account 0007', no real
-- names/emails/card numbers). Safely re-runnable: this script always resets
-- the table to the same 10-row starting state, which is how the experiment
-- resets between the six captured runs so one run cannot contaminate the
-- next (the phantom-read scenario inserts an 11th row during some runs;
-- re-running this script removes it again).

USE IsolationLab;
GO

DELETE FROM dbo.Accounts;
GO

INSERT INTO dbo.Accounts (Id, AccountName, Balance, Category) VALUES
    (1,  N'Account 0001', 1000.00, 'Retail'),
    (2,  N'Account 0002', 1500.00, 'Retail'),
    (3,  N'Account 0003', 2000.00, 'Retail'),
    (4,  N'Account 0004', 2500.00, 'Wholesale'),
    (5,  N'Account 0005', 3000.00, 'Wholesale'),
    (6,  N'Account 0006', 3500.00, 'Wholesale'),
    (7,  N'Account 0007', 4000.00, 'Retail'),
    (8,  N'Account 0008', 4500.00, 'Retail'),
    (9,  N'Account 0009', 5000.00, 'Wholesale'),
    (10, N'Account 0010', 5500.00, 'Wholesale');
GO
