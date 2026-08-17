using Xunit;

namespace Task1.Tests;

public class AuthorQuoteSummaryTests
{
    [Fact]
    public void ZeroQuoteAuthor_AppearsWithCountZero_NotMissing()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        var confucius = Assert.Single(rows, r => r.AuthorName == "Confucius");
        Assert.Equal(0, confucius.QuoteCount);
        Assert.Null(confucius.MostRecentQuoteText);
        Assert.Null(confucius.MostRecentQuoteCreatedAt);
    }

    [Fact]
    public void EveryAuthor_IsPresentExactlyOnce()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        Assert.Equal(10, rows.Count);
        Assert.Equal(10, rows.Select(r => r.AuthorId).Distinct().Count());
    }

    [Fact]
    public void TiedMostRecentQuotes_ResolveDeterministicallyByHighestId()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        // Quotes 9 and 10 for Marcus Aurelius both carry CreatedAt 2023-08-01T09:00:00 --
        // the ORDER BY CreatedAt DESC, Id DESC tie-break must pick Id 10.
        var marcus = Assert.Single(rows, r => r.AuthorName == "Marcus Aurelius");
        Assert.Equal("Very little is needed to make a happy life.", marcus.MostRecentQuoteText);
        Assert.Equal("2023-08-01T09:00:00", marcus.MostRecentQuoteCreatedAt);
    }

    [Fact]
    public void QuoteCounts_SumToActualTotalQuoteRowCount()
    {
        using var db = TestDatabase.Create();

        using var countCmd = db.Connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Quotes;";
        var totalQuotes = Convert.ToInt32(countCmd.ExecuteScalar());

        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        Assert.Equal(totalQuotes, rows.Sum(r => r.QuoteCount));
    }
}
