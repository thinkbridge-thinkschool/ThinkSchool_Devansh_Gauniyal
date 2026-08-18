USE IndexLab;
GO

-- OrderDate is chosen as the clustering key because Q1's dominant access
-- pattern is a date-range scan: clustering physically orders rows by
-- OrderDate, so a range predicate becomes a contiguous sequential read
-- instead of a scan of the entire heap in insertion order.
CREATE CLUSTERED INDEX CIX_Orders_OrderDate
    ON dbo.Orders (OrderDate);
GO
