-- Recursive CTE walking the Authors.InfluencedByAuthorId self-reference back to each
-- author's root ancestor, returning the author, their root ancestor, and the depth of
-- the chain between them.
-- SQLite requires the WITH RECURSIVE keyword for this; T-SQL/SQL Server accepts a plain
-- WITH for the same recursive form (RECURSIVE is implied there). A non-recursive CTE
-- (RankedChain, below) can still share the same WITH RECURSIVE clause -- the keyword
-- applies to the whole clause, not to each individual CTE.
--
-- Depth cap: "ic.Depth < 20" below guards against a cycle in InfluencedByAuthorId (e.g.
-- author A influenced-by B, and B influenced-by A). SQLite has no built-in cycle
-- detection for a self-referencing walk like this one -- without a cap, such a cycle
-- would make the recursive step re-fire forever and never terminate.
WITH RECURSIVE InfluenceChain (AuthorId, AuthorName, AncestorId, Depth) AS (
    -- Anchor: every author starts as their own ancestor at depth 0.
    SELECT
        Id,
        Name,
        Id,
        0
    FROM Authors

    UNION ALL

    -- Recursive step: walk exactly one InfluencedByAuthorId link per iteration.
    SELECT
        ic.AuthorId,
        ic.AuthorName,
        a.InfluencedByAuthorId,
        ic.Depth + 1
    FROM InfluenceChain ic
    INNER JOIN Authors a ON a.Id = ic.AncestorId
    WHERE a.InfluencedByAuthorId IS NOT NULL
      AND ic.Depth < 20
),
RankedChain AS (
    -- The last row produced per author (highest Depth) is their root ancestor.
    SELECT
        AuthorId,
        AuthorName,
        AncestorId,
        Depth,
        ROW_NUMBER() OVER (PARTITION BY AuthorId ORDER BY Depth DESC) AS rn
    FROM InfluenceChain
)
SELECT
    rc.AuthorId,
    rc.AuthorName,
    rc.AncestorId AS RootAncestorId,
    root.Name AS RootAncestorName,
    rc.Depth
FROM RankedChain rc
INNER JOIN Authors root ON root.Id = rc.AncestorId
WHERE rc.rn = 1
ORDER BY rc.AuthorName;
