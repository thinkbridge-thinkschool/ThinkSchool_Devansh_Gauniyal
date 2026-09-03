# Day 21, Task 1 — HybridCache + stampede protection

## Notes for mentor

### Cache wiring

```csharp
// Registration (Program.cs)
builder.Services.AddSingleton<DbQueryCounter>();

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(10)
    };
});

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var redisConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
redisConfigurationOptions.AbortOnConnectFail = false;
redisConfigurationOptions.ConnectTimeout = 1000;
redisConfigurationOptions.ConnectRetry = 1;
redisConfigurationOptions.SyncTimeout = 1000;
redisConfigurationOptions.AsyncTimeout = 1000;
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.ConfigurationOptions = redisConfigurationOptions;
    options.InstanceName = "day21-hybridcache:";
});

builder.Services.AddSingleton<AuthorQuoteSummaryCacheService>();
```

```csharp
// The wrapper (QuotesApi/Caching/AuthorQuoteSummaryCacheService.cs)
private static string BuildKey(string variant) => $"authors:quote-summary:{variant}";

public ValueTask<List<AuthorQuoteSummary>> GetSummaryAsync(
    string performanceDbPath, string variant, CancellationToken cancellationToken) =>
    cache.GetOrCreateAsync(
        BuildKey(variant),
        (performanceDbPath, counter, options),
        static (state, ct) => new ValueTask<List<AuthorQuoteSummary>>(
            AuthorQuoteSummaryReader.ReadAsync(state.performanceDbPath, state.counter, state.options, ct)),
        tags: [Tag],
        cancellationToken: cancellationToken);
```

### Load test — before / after (real measured numbers)

20 concurrency, 10s duration, bombardier, against `GET /api/authors/quote-summary/{cached,uncached}`:

| Path | Total requests | Real DB queries | DB queries/sec | p99 latency |
|---|---:|---:|---:|---:|
| Uncached | 977 | 49,827 | 4,982.70 | 777.14ms |
| Cached | 656,316 | 51 | 5.10 | 1.25ms |

**Cache hit rate: 100.0%** (656,315 of 656,316 requests served without a DB round trip — the cache starts on a cold key, the first concurrent wave coalesces into one factory run, every request after that is a hit). Full raw bombardier output and the generated summary are in `output/`.

### Stampede protection under concurrency

`CachingTests.ConcurrentRequests_SameColdKey_ProduceExactlyOneFactoryRun`: 40 concurrent requests, same cold key, one shared `HybridCache` → **1 factory run (51 DB queries), not 40 × 51 = 2,040**. Required mutation check (append a per-call `Guid` to the cache key, defeating coalescing): the same test then failed with `Expected: 51, Actual: 2040` — confirming the test genuinely detects a broken stampede guard. Reverted, suite green again (28/28).

### Scope resolutions

- The hot read is a copy of `day-3/task-3`'s `QuotesApi` (`GET /api/authors/quote-summary`, real EF Core/SQLite query); the original is byte-for-byte untouched — copying was necessary because six other tasks (`day-4/task-2,4,5,6,7`, `day-11/task-1`) hold a `ProjectReference` straight at it.
- These are local, single-machine numbers with a 150ms artificial DB delay standing in for a slow query — not a production benchmark.

## What did you learn this session?

That HybridCache's stampede coalescing is genuinely automatic — I didn't write any locking, and 40 concurrent cold callers still produced exactly one DB round trip. I also learned it treats reads and tag-based removal differently: a Redis outage degrades reads to local-only cache silently, but it makes reset (tag removal) throw, which I had to catch myself.

## What would break this?

Two cache instances with different expiration settings, or a typo in the key-building string, would silently split what should be one cache entry into several — the summary would look fine, just each replica would recompute on its own schedule.
