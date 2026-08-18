USE IndexLab;
GO

-- Write-side cost measurement: insert 10,000 further synthetic rows,
-- wrapped in SET STATISTICS IO/TIME, then delete exactly those rows so
-- the table returns to its ~100,000-row baseline and repeated runs (e.g.
-- once with only the clustered index, once with all three indexes) stay
-- comparable. Row values are deterministic, continuing the same
-- arithmetic scheme as 02_generate_data.sql, offset from the current max
-- OrderId so this script is safely re-runnable.
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

DECLARE @StartN INT = (SELECT ISNULL(MAX(OrderId), 0) FROM dbo.Orders);

;WITH L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
Numbers AS
(
    SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM L3 A CROSS JOIN L3 B
)
INSERT INTO dbo.Orders (OrderDate, CustomerId, Status, Amount, Description)
SELECT
    DATEADD(DAY, (n + @StartN) % 1826, '2021-01-01'),
    ((n + @StartN) % 5000) + 1,
    CASE
        WHEN (n + @StartN) % 100 < 70 THEN 'Completed'
        WHEN (n + @StartN) % 100 < 85 THEN 'Pending'
        WHEN (n + @StartN) % 100 < 95 THEN 'Cancelled'
        WHEN (n + @StartN) % 100 < 99 THEN 'Returned'
        ELSE 'Disputed'
    END,
    CAST((((n + @StartN) % 50000) + 1) AS DECIMAL(10,2)) / 100.0,
    'Synthetic write-cost row #' + RIGHT('000000' + CAST(n AS VARCHAR(6)), 6)
        + ' - ' + REPLICATE('x', 245)
FROM Numbers;

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO

-- Clean up: remove the rows just inserted so the table returns to its
-- ~100,000-row baseline and subsequent runs are comparable.
DECLARE @Cutoff INT = (SELECT MAX(OrderId) - 10000 FROM dbo.Orders);
DELETE FROM dbo.Orders WHERE OrderId > @Cutoff;
GO
