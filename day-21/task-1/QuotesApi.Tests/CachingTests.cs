using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace QuotesApi.Tests;

// Covers the Day 21 Task 1 caching surface added on top of the copied day-3/task-3
// QuotesApi. Concurrency tests use a Barrier to force all callers to dispatch their
// request at (as near as the runtime allows) the same instant, combined with a wide
// artificial DB delay (configured per test, see CachingApiFactory) so every caller is
// guaranteed to be in flight before the first one completes - no Thread.Sleep is used
// as a substitute for real synchronization anywhere in this file. The one place a real
// delay is unavoidable is the expiration test, where the thing under test is the
// passage of real time; that delay is generous (400ms against a 150ms TTL) to stay
// robust against machine load rather than racing the clock.
public sealed class CachingTests
{
    private static async Task<int> GetCountAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<CountResponse>("/api/measurement/db-query-count");
        return body!.Count;
    }

    private static async Task ResetAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/measurement/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    private sealed record CountResponse(int Count);

    [Fact]
    public async Task ColdRead_PopulatesCache_AndHitsDbExactlyOnce()
    {
        using var factory = new CachingApiFactory(new() { ["Measurement:ArtificialDbDelayMs"] = "0" });
        using var client = factory.CreateClient();
        var key = $"miss-{Guid.NewGuid():N}";

        var response = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
        response.EnsureSuccessStatusCode();

        // 1 query for all authors + 50 explicit Collection().Load() calls, the N+1
        // shape AuthorQuoteSummaryQuery deliberately has for 50 seeded authors.
        Assert.Equal(51, await GetCountAsync(client));
    }

    [Fact]
    public async Task WarmRead_ServesFromCache_WithNoAdditionalDbQuery()
    {
        using var factory = new CachingApiFactory(new() { ["Measurement:ArtificialDbDelayMs"] = "0" });
        using var client = factory.CreateClient();
        var key = $"warm-{Guid.NewGuid():N}";

        await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
        var afterFirst = await GetCountAsync(client);

        var second = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
        second.EnsureSuccessStatusCode();

        Assert.Equal(afterFirst, await GetCountAsync(client));
    }

    [Fact]
    public async Task ConcurrentRequests_SameColdKey_ProduceExactlyOneFactoryRun()
    {
        const int concurrency = 40;
        using var factory = new CachingApiFactory(new()
        {
            // Wide relative to local in-process dispatch time, so all 40 callers are
            // guaranteed to reach the cache before the first factory run completes.
            ["Measurement:ArtificialDbDelayMs"] = "300"
        });
        var key = $"stampede-{Guid.NewGuid():N}";

        // Force the TestServer to boot on this thread before firing 40 concurrent
        // first-time CreateClient() calls at it. Without this, the failure this test
        // caught wasn't in the cache at all: WebApplicationFactory's lazy host-start
        // isn't safe to trigger from many threads at once, so multiple independent
        // Program.cs boots raced to create/seed the *same* SQLite file and collided
        // ("table Authors already exists") - a test-harness bug, not a caching bug.
        // ConcurrentRequests_UncachedPath_ProduceOneFactoryRunPerCaller avoided this
        // by already calling CreateClient() once before its own concurrent burst.
        using var warmupClient = factory.CreateClient();

        using var barrier = new Barrier(concurrency);

        // Task.Run is essential here, not decoration: Enumerable.Select is lazy, so
        // Task.WhenAll over a bare `async _ => ...` sequence would invoke each lambda
        // synchronously, one at a time, on the calling thread as it enumerates - the
        // first call would block forever on SignalAndWait() before the second lambda
        // ever got a chance to run (confirmed the hard way: an earlier version without
        // Task.Run deadlocked the whole run). Task.Run dispatches every iteration to
        // the thread pool immediately, so all `concurrency` callers are genuinely
        // in flight before any of them reaches the barrier.
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            using var client = factory.CreateClient();
            barrier.SignalAndWait();
            var response = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
            response.EnsureSuccessStatusCode();
        }));

        await Task.WhenAll(tasks);

        using var verifyClient = factory.CreateClient();
        // One factory run for all 40 concurrent callers, not 40 x 51: this is the
        // stampede-protection assertion the whole task exists to prove.
        Assert.Equal(51, await GetCountAsync(verifyClient));
    }

    [Fact]
    public async Task ConcurrentRequests_UncachedPath_ProduceOneFactoryRunPerCaller()
    {
        const int concurrency = 10;
        using var factory = new CachingApiFactory(new() { ["Measurement:ArtificialDbDelayMs"] = "0" });

        // Seed once and reset the counter before the timed/counted phase, so the
        // lazy first-hit seeding (which itself reads the DB) isn't mixed into the count.
        using (var seedClient = factory.CreateClient())
        {
            await seedClient.GetAsync("/api/authors/quote-summary/uncached");
        }
        using (var resetClient = factory.CreateClient())
        {
            await ResetAsync(resetClient);
        }

        using var barrier = new Barrier(concurrency);
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            using var client = factory.CreateClient();
            barrier.SignalAndWait();
            var response = await client.GetAsync("/api/authors/quote-summary/uncached");
            response.EnsureSuccessStatusCode();
        }));

        await Task.WhenAll(tasks);

        using var verifyClient = factory.CreateClient();
        // No cache in front of this endpoint: every one of the 10 concurrent callers
        // runs the query independently, proving the contrast with the test above is real.
        Assert.Equal(concurrency * 51, await GetCountAsync(verifyClient));
    }

    [Fact]
    public async Task DifferentCacheKeys_AreNotCoalescedTogether()
    {
        using var factory = new CachingApiFactory(new() { ["Measurement:ArtificialDbDelayMs"] = "0" });
        using var client = factory.CreateClient();

        await client.GetAsync("/api/authors/quote-summary/cached?key=key-a");
        Assert.Equal(51, await GetCountAsync(client));

        // A different key is a genuinely different cache entry, not coalesced with key-a.
        await client.GetAsync("/api/authors/quote-summary/cached?key=key-b");
        Assert.Equal(102, await GetCountAsync(client));

        // key-a is still warm from its own earlier read.
        await client.GetAsync("/api/authors/quote-summary/cached?key=key-a");
        Assert.Equal(102, await GetCountAsync(client));
    }

    [Fact]
    public async Task Expiration_CausesRefetch_AfterConfiguredWindow()
    {
        using var factory = new CachingApiFactory();
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
        var key = $"expiring-{Guid.NewGuid():N}";
        var callCount = 0;
        var shortOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMilliseconds(150),
            LocalCacheExpiration = TimeSpan.FromMilliseconds(150)
        };

        ValueTask<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.FromResult(42);
        }

        await cache.GetOrCreateAsync(key, ct => Factory(ct), shortOptions);
        Assert.Equal(1, callCount);

        await cache.GetOrCreateAsync(key, ct => Factory(ct), shortOptions);
        Assert.Equal(1, callCount); // still within the window: no refetch

        await Task.Delay(TimeSpan.FromMilliseconds(400));

        await cache.GetOrCreateAsync(key, ct => Factory(ct), shortOptions);
        Assert.Equal(2, callCount); // past Expiration: a genuine refetch
    }

    [Fact]
    public async Task FactoryThatThrows_DoesNotPoisonTheCache()
    {
        using var factory = new CachingApiFactory();
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
        var key = $"throws-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<int>(key, static _ => throw new InvalidOperationException("factory boom")).AsTask());

        // A working factory afterwards must genuinely run - not return a cached
        // default/empty value left behind by the failed attempt.
        var value = await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(42));
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task RedisUnavailable_CachedReadsStillSucceed_DegradingToL1()
    {
        using var factory = new CachingApiFactory(new()
        {
            // Nothing listens on this loopback port - deterministic, and independent
            // of whatever state the shared local Redis container happens to be in.
            ["Redis:ConnectionString"] = "127.0.0.1:1",
            ["Measurement:ArtificialDbDelayMs"] = "0"
        });
        using var client = factory.CreateClient();
        var key = $"redis-down-{Guid.NewGuid():N}";

        var first = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
        first.EnsureSuccessStatusCode();

        var second = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");
        second.EnsureSuccessStatusCode();

        // Still only one DB round trip: L1 alone still caches and coalesces even with
        // L2 completely unreachable, matching the documented degrade behaviour.
        Assert.Equal(51, await GetCountAsync(client));
    }

    [Fact]
    public async Task RedisUnavailable_ResetStillSucceeds_AndResetsTheCounter()
    {
        // Regression test for a real bug this task caught: RemoveByTagAsync used to
        // propagate StackExchange.Redis.RedisConnectionException when L2 was down,
        // turning /api/measurement/reset into a 500. See PROVENANCE.md and README.md
        // for the verification log. AuthorQuoteSummaryCacheService.EvictAsync now
        // catches it.
        using var factory = new CachingApiFactory(new()
        {
            ["Redis:ConnectionString"] = "127.0.0.1:1",
            ["Measurement:ArtificialDbDelayMs"] = "0"
        });
        using var client = factory.CreateClient();
        await client.GetAsync("/api/authors/quote-summary/uncached");

        var response = await client.PostAsync("/api/measurement/reset", content: null);
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, await GetCountAsync(client));
    }
}
