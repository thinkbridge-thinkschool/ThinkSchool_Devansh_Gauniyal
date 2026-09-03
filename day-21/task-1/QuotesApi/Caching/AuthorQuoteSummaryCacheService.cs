using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Performance;
using StackExchange.Redis;

namespace QuotesApi.Caching;

// Wraps HybridCache.GetOrCreateAsync for exactly one read: the author/quote summary.
// The cache key is built explicitly from a single "variant" string (default "default"
// for the one real endpoint; tests use other variants to prove distinct keys are
// never coalesced together) - key construction is the single most common defect in
// caching code, so it stays in one obvious place instead of being inlined per call site.
public sealed class AuthorQuoteSummaryCacheService(
    HybridCache cache,
    DbQueryCounter counter,
    MeasurementOptions options,
    ILogger<AuthorQuoteSummaryCacheService> logger)
{
    public const string Tag = "authors-quote-summary";

    private static string BuildKey(string variant) => $"authors:quote-summary:{variant}";

    public ValueTask<List<AuthorQuoteSummary>> GetSummaryAsync(
        string performanceDbPath,
        string variant,
        CancellationToken cancellationToken)
    {
        return cache.GetOrCreateAsync(
            BuildKey(variant),
            (performanceDbPath, counter, options),
            static (state, ct) => new ValueTask<List<AuthorQuoteSummary>>(
                AuthorQuoteSummaryReader.ReadAsync(state.performanceDbPath, state.counter, state.options, ct)),
            tags: [Tag],
            cancellationToken: cancellationToken);
    }

    // Verification note (see README.md "when Redis is down"): a cache READ miss
    // degrades to L1+DB on its own, because HybridCache itself catches an L2 failure
    // on that path. Tag-based REMOVAL does not get the same treatment - it needs the
    // distributed L2 to coordinate invalidation across instances, so when Redis is
    // unreachable HybridCache raises the real StackExchange.Redis exception instead of
    // swallowing it. Caught here so a reset click/call during a Redis outage still
    // resets the DB-query counter and returns 200 instead of 500; the trade-off is
    // that any entry already sitting in L2 for this tag cannot be proven invalidated
    // until Redis comes back, or until its own Expiration elapses.
    public async ValueTask EvictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveByTagAsync(Tag, cancellationToken);
        }
        catch (RedisConnectionException exception)
        {
            logger.LogWarning(
                exception,
                "RemoveByTagAsync for tag {Tag} failed because Redis (L2) is unreachable; " +
                "the DB-query counter is still reset, but any entry already cached in L2 " +
                "cannot be proven invalidated until Redis is back or its own expiration elapses.",
                Tag);
        }
    }
}
