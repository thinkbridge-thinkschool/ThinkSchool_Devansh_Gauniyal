namespace DapperComparison.Tests;

public class EquivalenceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public EquivalenceTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void All_three_variants_return_the_same_row_count()
    {
        var tracked = RunTracked();
        var projection = RunProjection();
        var dapper = RunDapper();

        Assert.Equal(Seeder.QuoteCount, tracked.Count);
        Assert.Equal(tracked.Count, projection.Count);
        Assert.Equal(tracked.Count, dapper.Count);
    }

    [Fact]
    public void All_three_variants_return_identical_rows_in_identical_order()
    {
        var tracked = RunTracked();
        var projection = RunProjection();
        var dapper = RunDapper();

        Assert.Equal(projection, tracked);
        Assert.Equal(projection, dapper);
    }

    [Fact]
    public void Dapper_rows_carry_the_denormalized_author_name_and_country()
    {
        var dapper = RunDapper();

        Assert.NotEmpty(dapper);
        Assert.All(dapper, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.AuthorName));
            Assert.False(string.IsNullOrWhiteSpace(item.AuthorCountry));
            Assert.False(string.IsNullOrWhiteSpace(item.QuoteText));
            Assert.False(string.IsNullOrWhiteSpace(item.SubmittedOn));
        });
    }

    private List<QuoteWallItem> RunTracked()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        return EfQueries.RunTracked(context, Comparison.SubmittedSinceUtc);
    }

    private List<QuoteWallItem> RunProjection()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        return EfQueries.RunProjection(context, Comparison.SubmittedSinceUtc);
    }

    private List<QuoteWallItem> RunDapper() => DapperQueries.Run(_fixture.DbPath, Comparison.SubmittedSinceUtc);
}
