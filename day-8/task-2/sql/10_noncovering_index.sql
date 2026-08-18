USE CoveringLab;
GO

-- BEFORE state: a plain non-clustered index on the filter column only, no
-- INCLUDE. This is expected to force a Key Lookup for 03_query.sql, since
-- OrderDate/Amount/Status are not held anywhere in this index.
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId);
GO
