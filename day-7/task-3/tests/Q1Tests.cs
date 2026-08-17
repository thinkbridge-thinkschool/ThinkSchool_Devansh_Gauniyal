using Xunit;

namespace Task3.Tests;

public class Q1Tests
{
    [Fact]
    public void ReturnsExactlyTheExpectedAuthorSet()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "10_q1_authors_with_quotes_no_tags.sql");

        // Only Wilder Voss has quotes and zero tags across all of them.
        Assert.Equal(new HashSet<string> { "Wilder Voss" }, new HashSet<string>(names));
    }

    [Fact]
    public void ZeroQuoteAuthor_IsAbsent()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "10_q1_authors_with_quotes_no_tags.sql");

        Assert.DoesNotContain("Freya Lindqvist", names);
    }

    [Fact]
    public void FullyTaggedAuthor_IsAbsent()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "10_q1_authors_with_quotes_no_tags.sql");

        Assert.DoesNotContain("Marguerite Holt", names);
    }

    [Fact]
    public void PartiallyTaggedAuthor_IsAbsent_PerTheDocumentedNoTagsAtAllReading()
    {
        // Otis Bramwell has 2 tagged and 2 untagged quotes. Under the documented reading
        // ("no tags at all" means zero tags across every one of the author's quotes), he
        // does NOT qualify -- he has some tags, even though not all of his quotes carry one.
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "10_q1_authors_with_quotes_no_tags.sql");

        Assert.DoesNotContain("Otis Bramwell", names);
    }

    [Fact]
    public void ReturnsNoDuplicateRows()
    {
        using var db = TestDatabase.Create();
        var names = SingleColumnQuery.Execute(db.Connection, "10_q1_authors_with_quotes_no_tags.sql");

        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
