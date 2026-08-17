using Xunit;

namespace Task3.Tests;

public class Q2Tests
{
    [Fact]
    public void ReturnsExactlyTheBothCategoriesAuthors()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "11_q2_authors_in_both_sets.sql");

        // Only Anouk Fenn has at least one classic-tagged quote and at least one
        // modern-tagged quote.
        Assert.Equal(new HashSet<string> { "Anouk Fenn" }, new HashSet<string>(names));
    }

    [Fact]
    public void ClassicOnlyAuthor_IsExcluded()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "11_q2_authors_in_both_sets.sql");

        Assert.DoesNotContain("Callista Wren", names);
    }

    [Fact]
    public void ModernOnlyAuthors_AreExcluded()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "11_q2_authors_in_both_sets.sql");

        Assert.DoesNotContain("Percival Doyle", names);
        Assert.DoesNotContain("Solomon Vance", names);
    }

    [Fact]
    public void ReturnsNoDuplicateRows()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "11_q2_authors_in_both_sets.sql");

        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
