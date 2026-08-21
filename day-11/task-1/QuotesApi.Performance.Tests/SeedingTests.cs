namespace QuotesApi.Performance.Tests;

public class SeedingTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Seeding_produces_expected_author_count()
    {
        using var context = new PerformanceDbContext(_fixture.DbPath);

        Assert.Equal(PerformanceSeeder.AuthorCount, context.Authors.Count());
    }

    [Fact]
    public void Seeding_produces_expected_quote_count()
    {
        using var context = new PerformanceDbContext(_fixture.DbPath);

        Assert.Equal(PerformanceSeeder.QuoteCount, context.Quotes.Count());
    }

    [Fact]
    public void Seeding_is_safely_rerunnable_without_changing_row_counts()
    {
        using var context = new PerformanceDbContext(_fixture.DbPath);

        PerformanceSeeder.SeedIfNeeded(context);
        PerformanceSeeder.SeedIfNeeded(context);

        Assert.Equal(PerformanceSeeder.AuthorCount, context.Authors.Count());
        Assert.Equal(PerformanceSeeder.QuoteCount, context.Quotes.Count());
    }

    public void Dispose() => _fixture.Dispose();
}
