using Xunit;

namespace Task1.Tests;

public class AuthorQuoteSummaryTests
{
    [Fact]
    public void ZeroQuoteAuthor_AppearsWithCountZero_NotMissing_NotNull()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        // Confucius (Id 7) has zero rows in Quotes. If the LEFT JOIN / COALESCE were
        // dropped for an INNER JOIN, this row would vanish entirely instead of showing 0.
        var confucius = Assert.Single(rows, r => r.AuthorName == "Confucius");
        Assert.Equal(0, confucius.QuoteCount);
        Assert.Null(confucius.MostRecentQuoteText);
        Assert.Null(confucius.MostRecentQuoteCreatedAt);
    }

    [Fact]
    public void EveryAuthor_IsPresentExactlyOnce()
    {
        using var db = TestDatabase.Create();

        using var authorCountCmd = db.Connection.CreateCommand();
        authorCountCmd.CommandText = "SELECT COUNT(*) FROM Authors;";
        var authorCount = Convert.ToInt32(authorCountCmd.ExecuteScalar());

        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        Assert.Equal(authorCount, rows.Count);
        Assert.Equal(authorCount, rows.Select(r => r.AuthorId).Distinct().Count());
    }

    [Fact]
    public void MostRecentQuote_IsCorrectForANormalNonTiedAuthor()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        // Seneca (Id 1) has three quotes with distinct timestamps -- no tie involved here,
        // unlike Marcus Aurelius below. His latest is Quote 3 at 2023-06-05T14:20:00.
        var seneca = Assert.Single(rows, r => r.AuthorName == "Seneca");
        Assert.Equal(3, seneca.QuoteCount);
        Assert.Equal(
            "It is not that we have a short time to live, but that we waste a lot of it.",
            seneca.MostRecentQuoteText);
        Assert.Equal("2023-06-05T14:20:00", seneca.MostRecentQuoteCreatedAt);
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
    public void PerAuthorQuoteCounts_MatchIndependentlyComputedExpectation()
    {
        using var db = TestDatabase.Create();

        // Computed directly from Quotes, independently of 20_author_quote_summary.sql,
        // as the ground truth to check the graded query's QuoteCount column against.
        var expectedCounts = new Dictionary<int, int>();
        using (var independentCmd = db.Connection.CreateCommand())
        {
            independentCmd.CommandText = "SELECT AuthorId, COUNT(*) FROM Quotes GROUP BY AuthorId;";
            using var reader = independentCmd.ExecuteReader();
            while (reader.Read())
            {
                expectedCounts[reader.GetInt32(0)] = reader.GetInt32(1);
            }
        }

        var rows = AuthorQuoteSummaryQuery.Execute(db.Connection);

        foreach (var row in rows)
        {
            var expected = expectedCounts.GetValueOrDefault(row.AuthorId, 0);
            Assert.Equal(expected, row.QuoteCount);
        }
    }
}
