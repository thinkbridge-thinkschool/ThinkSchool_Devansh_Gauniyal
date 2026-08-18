USE IndexLab;
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
    DROP TABLE dbo.Orders;
GO

-- Stage 0 baseline: a heap. No primary key, no clustered index, no
-- nonclustered index of any kind. Description is a fixed-width CHAR(300)
-- so every row consumes a predictable amount of page space, which is what
-- makes the page-count / logical-reads story visible instead of noise.
CREATE TABLE dbo.Orders
(
    OrderId     INT           IDENTITY(1,1) NOT NULL,
    OrderDate   DATETIME2(0)  NOT NULL,
    CustomerId  INT           NOT NULL,
    Status      VARCHAR(20)   NOT NULL,
    Amount      DECIMAL(10,2) NOT NULL,
    Description CHAR(300)     NOT NULL
);
GO
