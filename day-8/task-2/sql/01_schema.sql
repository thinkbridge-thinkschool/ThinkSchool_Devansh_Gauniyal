USE CoveringLab;
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
    DROP TABLE dbo.Orders;
GO

-- This experiment is about non-clustered covering behaviour, not about
-- clustering choice, so the clustered index is part of the starting state
-- rather than a stage: OrderId is the surrogate key and its PRIMARY KEY
-- constraint is clustered by default. Description is a fixed-width
-- CHAR(300) so every row consumes a predictable amount of page space,
-- which is what makes the Key Lookup cost visible instead of noise.
CREATE TABLE dbo.Orders
(
    OrderId     INT           IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    CustomerId  INT           NOT NULL,
    OrderDate   DATETIME2(0)  NOT NULL,
    Status      VARCHAR(20)   NOT NULL,
    Amount      DECIMAL(10,2) NOT NULL,
    Description CHAR(300)     NOT NULL
);
GO
