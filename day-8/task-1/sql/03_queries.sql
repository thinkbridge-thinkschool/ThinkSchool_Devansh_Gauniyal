USE IndexLab;
GO

-- Q1: targets 10_clustered_index.sql (CIX_Orders_OrderDate).
-- Date-range predicate on the clustering key: before the clustered index
-- exists this is a full heap scan; after it exists, it is a contiguous
-- range scan/seek over pages ordered by OrderDate.
SELECT OrderId, OrderDate, CustomerId, Amount
FROM dbo.Orders
WHERE OrderDate >= '2023-06-01' AND OrderDate < '2023-07-01';
GO

-- Q2: targets 11_nonclustered_customer.sql (IX_Orders_CustomerId).
-- Equality lookup on CustomerId that also selects Description, a column
-- never included in any index in this experiment. Once the nonclustered
-- index on CustomerId exists, SQL Server can seek it but must still
-- perform a Key Lookup into the clustered index to fetch Description
-- (and the other non-key columns) for each matching row.
SELECT OrderId, OrderDate, Amount, Description
FROM dbo.Orders
WHERE CustomerId = 1234;
GO

-- Q3: targets 12_nonclustered_covering.sql (IX_Orders_CustomerId_Covering).
-- Same equality shape as Q2, but the predicate (CustomerId) and every
-- selected column (CustomerId, Amount, Status) are all present in the
-- covering index's key + INCLUDE list, so once that index exists the
-- Key Lookup disappears entirely.
SELECT CustomerId, Amount, Status
FROM dbo.Orders
WHERE CustomerId = 1234;
GO
