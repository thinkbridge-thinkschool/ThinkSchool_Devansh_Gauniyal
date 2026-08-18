USE IndexLab;
GO

-- Adds Amount and Status as INCLUDE columns so a query that filters on
-- CustomerId and only needs CustomerId/Amount/Status (Q3) is fully
-- satisfied by this index alone -- no Key Lookup required.
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Covering
    ON dbo.Orders (CustomerId)
    INCLUDE (Amount, Status);
GO
