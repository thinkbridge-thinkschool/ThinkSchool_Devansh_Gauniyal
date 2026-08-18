USE CoveringLab;
GO

-- Deterministic synthetic data: ~100,000 rows generated purely from
-- row-number arithmetic. No NEWID()/RAND(), so re-running this script
-- (after 01_schema.sql has recreated the table) produces byte-identical
-- data every time. CustomerId cycles through 1..5000, so a single-customer
-- filter matches ~20 rows -- enough that the Key Lookup cost is clearly
-- visible, not one or two rows.
;WITH L0 AS (SELECT 1 AS c UNION ALL SELECT 1),                 -- 2 rows
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),                 -- 4 rows
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),                 -- 16 rows
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),                 -- 256 rows
L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),                 -- 65,536 rows
Numbers AS
(
    SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM L4 A CROSS JOIN L1 B                                    -- 65,536 * 4 = 262,144 rows
)
INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Description)
SELECT
    (n % 5000) + 1,
    DATEADD(DAY, n % 1826, '2021-01-01'),
    CASE
        WHEN n % 100 < 70 THEN 'Completed'
        WHEN n % 100 < 85 THEN 'Pending'
        WHEN n % 100 < 95 THEN 'Cancelled'
        WHEN n % 100 < 99 THEN 'Returned'
        ELSE 'Disputed'
    END,
    CAST(((n % 50000) + 1) AS DECIMAL(10,2)) / 100.0,
    'Synthetic order row #' + RIGHT('000000' + CAST(n AS VARCHAR(6)), 6)
        + ' - ' + REPLICATE('x', 250)
FROM Numbers;
GO

SELECT COUNT(*) AS TotalRows FROM dbo.Orders;
GO

-- How many rows the test query (03_query.sql) actually matches -- context
-- for judging the logical-reads numbers.
SELECT COUNT(*) AS MatchedRowsForTestCustomer FROM dbo.Orders WHERE CustomerId = 1234;
GO
