USE CoveringLab;
GO

-- AFTER state: the SAME index, rebuilt in place via DROP_EXISTING rather
-- than creating a second, differently-named index. This mirrors real
-- practice, keeps the before/after comparison about one index rather than
-- two, and removes any doubt about which index the optimiser chose.
--
-- INCLUDE holds exactly the non-key columns 03_query.sql selects and
-- nothing else:
--   OrderDate -- selected by the query, not part of the index key
--   Amount    -- selected by the query, not part of the index key
--   Status    -- selected by the query, not part of the index key
-- (OrderId needs no INCLUDE: it is the clustering key and is already
-- carried as the row locator in every non-clustered index.)
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId)
    INCLUDE (OrderDate, Amount, Status)
    WITH (DROP_EXISTING = ON);
GO
