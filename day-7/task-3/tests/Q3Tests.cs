using Xunit;

namespace Task3.Tests;

public class Q3Tests
{
    [Fact]
    public void ReturnsCorrectDistinctCount()
    {
        using var db = TestDatabase.Create();

        // Computed directly against Tags, independently of 12_q3_combined_distinct_tags.sql,
        // as the ground truth for how many distinct tag names actually exist.
        using var independentCmd = db.Connection.CreateCommand();
        independentCmd.CommandText = "SELECT COUNT(DISTINCT Name) FROM Tags;";
        var expectedDistinctCount = Convert.ToInt32(independentCmd.ExecuteScalar());

        var names = SingleColumnQuery.Execute(db.Connection, "12_q3_combined_distinct_tags.sql");

        Assert.Equal(expectedDistinctCount, names.Count);
    }

    [Fact]
    public void UnionAll_WouldHaveReturnedStrictlyMoreRows()
    {
        // 'wisdom' is seeded as two Tag rows (classic and modern) -- UNION collapses that
        // duplicate name to one; UNION ALL would keep both, so it must return more rows.
        using var db = TestDatabase.Create();
        var contrasts = OperatorContrastsQuery.Execute(db.Connection);

        Assert.True(
            contrasts.UnionAllCount > contrasts.UnionCount,
            $"Expected UNION ALL ({contrasts.UnionAllCount}) to return strictly more rows than UNION ({contrasts.UnionCount}).");
    }

    [Fact]
    public void ReturnsNoDuplicateRows()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "12_q3_combined_distinct_tags.sql");

        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
