namespace FastApi.Tests;

public class QueryCountTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Projection_endpoint_executes_exactly_one_query()
    {
        var collector = new SqlLogCollector();
        using var context = new QuotesDbContext(_fixture.DbPath, collector);

        Queries.RunProjection(context);

        Assert.Equal(1, collector.ExecutedCommandCount);
    }

    [Fact]
    public void Split_query_endpoint_executes_a_small_fixed_query_count_that_does_not_scale_with_author_count()
    {
        var collector = new SqlLogCollector();
        using var context = new QuotesDbContext(_fixture.DbPath, collector);

        Queries.RunSplitQuery(context);

        // Real observed count: 2 (one for authors, one for their quotes via the buffered
        // join AsSplitQuery() produces) - fixed regardless of author count, nowhere near
        // 1 + N (51).
        Assert.Equal(2, collector.ExecutedCommandCount);
        Assert.True(collector.ExecutedCommandCount < Seeder.AuthorCount,
            $"Expected the split-query count ({collector.ExecutedCommandCount}) to stay well below the author count ({Seeder.AuthorCount}), proving it does not scale with N.");
    }

    [Fact]
    public void Reproduced_slow_endpoint_still_executes_one_plus_n_queries()
    {
        // Confirms the in-process comparison is honest: the workload here is byte-for-byte
        // the same N+1 pattern task-1 measured, so any difference the fixed variants show
        // is attributable to the fix, not a changed workload.
        var collector = new SqlLogCollector();
        using var context = new QuotesDbContext(_fixture.DbPath, collector);

        Queries.RunSlow(context);

        Assert.Equal(Seeder.AuthorCount + 1, collector.ExecutedCommandCount);
    }

    public void Dispose() => _fixture.Dispose();
}
