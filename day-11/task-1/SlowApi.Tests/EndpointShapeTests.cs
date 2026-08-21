namespace SlowApi.Tests;

// The slowness must come from the two anti-patterns, not from the endpoint being broken -
// these confirm the returned shape and counts are actually correct.
public class EndpointShapeTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Summary_contains_one_row_per_author()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var summary = AuthorQuoteSummaryQuery.Run(context);

        Assert.Equal(Seeder.AuthorCount, summary.Count);
    }

    [Fact]
    public void Summary_quote_counts_sum_to_total_quotes()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var summary = AuthorQuoteSummaryQuery.Run(context);

        Assert.Equal(Seeder.QuoteCount, summary.Sum(s => s.QuoteCount));
    }

    [Fact]
    public void Every_author_has_the_expected_quote_count()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var summary = AuthorQuoteSummaryQuery.Run(context);

        Assert.All(summary, s => Assert.Equal(Seeder.QuotesPerAuthor, s.QuoteCount));
    }

    public void Dispose() => _fixture.Dispose();
}
