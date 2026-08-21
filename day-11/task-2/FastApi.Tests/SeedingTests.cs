namespace FastApi.Tests;

// Asserts the SPECIFIC numbers task-1 used - a different data volume would invalidate the
// before/after comparison.
public class SeedingTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Seeded_author_count_matches_task1_exactly()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Assert.Equal(50, context.Authors.Count());
        Assert.Equal(Seeder.AuthorCount, context.Authors.Count());
    }

    [Fact]
    public void Seeded_quote_count_matches_task1_exactly()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Assert.Equal(5000, context.Quotes.Count());
        Assert.Equal(Seeder.QuoteCount, context.Quotes.Count());
    }

    [Fact]
    public void Seeding_is_safely_rerunnable_without_changing_row_counts()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        Seeder.SeedIfNeeded(context);
        Seeder.SeedIfNeeded(context);

        Assert.Equal(50, context.Authors.Count());
        Assert.Equal(5000, context.Quotes.Count());
    }

    public void Dispose() => _fixture.Dispose();
}
