USE CoveringLab;
GO

-- The query under test. It filters on CustomerId, which
-- 10_noncovering_index.sql keys, but selects OrderDate, Amount and Status --
-- none of which that index holds (OrderId is the clustering key and is
-- already present in every non-clustered index as the row locator, so it
-- doesn't force a lookup by itself). In the before state this forces SQL
-- Server to seek IX_Orders_CustomerId and then perform a Key Lookup back
-- into the clustered index for every matching row to fetch OrderDate,
-- Amount and Status.
SELECT OrderId, OrderDate, Amount, Status
FROM dbo.Orders
WHERE CustomerId = 1234;
GO
