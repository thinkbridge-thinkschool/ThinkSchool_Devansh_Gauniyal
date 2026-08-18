USE IndexLab;
GO

-- No INCLUDE columns on purpose: this index lets SQL Server seek directly
-- to the rows for a given CustomerId, but any column not in the key
-- (OrderId, OrderDate, Amount, Description, Status) still requires a Key
-- Lookup back into the clustered index. Q2 is designed to hit exactly
-- that Key Lookup cost.
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId);
GO
