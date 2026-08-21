namespace QuotesApi.Performance.Tests;

// The other core proof: invoking the endpoint's actual data-access path must execute
// exactly 1 + N queries (N = author count), counted from the real captured SQL log, not
// assumed from reading the source.
public class NPlusOneTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void Endpoint_data_access_executes_one_plus_n_queries()
    {
        var collector = new SqlLogCollector();
        using var context = new PerformanceDbContext(_fixture.DbPath, collector);

        AuthorQuoteSummaryQuery.Run(context);

        Assert.Equal(PerformanceSeeder.AuthorCount + 1, collector.ExecutedCommandCount);
    }

    public void Dispose() => _fixture.Dispose();
}
