namespace FastApi.Tests;

// A "fix" that returns less data than the original is not a fix. All three variants must
// return the exact same logical data: same author count, same total quote count, same
// field set, same per-author values.
public class EquivalenceTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void All_three_variants_return_the_same_number_of_authors()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var slow = Queries.RunSlow(context);
        var projection = Queries.RunProjection(context);
        var split = Queries.RunSplitQuery(context);

        Assert.Equal(Seeder.AuthorCount, slow.Count);
        Assert.Equal(slow.Count, projection.Count);
        Assert.Equal(slow.Count, split.Count);
    }

    [Fact]
    public void All_three_variants_sum_to_the_same_total_quote_count()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var slow = Queries.RunSlow(context);
        var projection = Queries.RunProjection(context);
        var split = Queries.RunSplitQuery(context);

        var slowTotal = slow.Sum(s => s.QuoteCount);
        Assert.Equal(Seeder.QuoteCount, slowTotal);
        Assert.Equal(slowTotal, projection.Sum(s => s.QuoteCount));
        Assert.Equal(slowTotal, split.Sum(s => s.QuoteCount));
    }

    [Fact]
    public void All_three_variants_return_identical_per_author_records()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        var slow = Queries.RunSlow(context).OrderBy(s => s.AuthorId).ToList();
        var projection = Queries.RunProjection(context).OrderBy(s => s.AuthorId).ToList();
        var split = Queries.RunSplitQuery(context).OrderBy(s => s.AuthorId).ToList();

        Assert.Equal(slow, projection);
        Assert.Equal(slow, split);
    }

    public void Dispose() => _fixture.Dispose();
}
