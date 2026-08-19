-- Day 9 / Task 2 — seed data for dbo.Accounts and dbo.Orders.
--
-- Small, obviously-synthetic rows only (names like 'Account 0001', order
-- descriptions like 'Synthetic order row' — no real names, emails, or
-- account numbers). Safely re-runnable: always resets both tables to the
-- same starting state, which is how the experiment resets between the
-- broken and fixed runs so one cannot contaminate the other.

USE DeadlockLab;
GO

DELETE FROM dbo.Accounts;
GO
DELETE FROM dbo.Orders;
GO

INSERT INTO dbo.Accounts (Id, AccountName, Balance) VALUES
    (1, N'Account 0001', 1000.00),
    (2, N'Account 0002', 2000.00);
GO

INSERT INTO dbo.Orders (Id, OrderDescription, OrderStatus) VALUES
    (1, N'Synthetic order row', 'Pending'),
    (2, N'Synthetic order row', 'Pending');
GO
