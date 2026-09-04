using QuotesApi.Performance;

namespace QuotesApi.Caching;

// The one real read path shared by both the cached and uncached endpoints, so the
// before/after comparison in Phase 7 is measuring the same query under the same
// artificial delay - the only difference between the two endpoints is whether a
// cache sits in front of this call.
public static class AuthorQuoteSummaryReader
{
    public static async Task<List<AuthorQuoteSummary>> ReadAsync(
        string performanceDbPath,
        DbQueryCounter counter,
        MeasurementOptions options,
        CancellationToken cancellationToken)
    {
        if (options.ArtificialDbDelayMs > 0)
        {
            await Task.Delay(options.ArtificialDbDelayMs, cancellationToken);
        }

        using var context = new CountingPerformanceDbContext(performanceDbPath, counter);
        return AuthorQuoteSummaryQuery.Run(context);
    }
}
