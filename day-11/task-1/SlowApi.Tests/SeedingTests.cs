namespace SlowApi.Tests;

public class SeedingTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Seeding_produces_expected_author_count()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Assert.Equal(Seeder.AuthorCount, context.Authors.Count());
    }

    [Fact]
    public void Seeding_produces_expected_quote_count()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Assert.Equal(Seeder.QuoteCount, context.Quotes.Count());
    }

    [Fact]
    public void Seeding_is_safely_rerunnable_without_changing_row_counts()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Seeder.SeedIfNeeded(context);
        Seeder.SeedIfNeeded(context);

        Assert.Equal(Seeder.AuthorCount, context.Authors.Count());
        Assert.Equal(Seeder.QuoteCount, context.Quotes.Count());
    }

    public void Dispose() => _fixture.Dispose();
}
