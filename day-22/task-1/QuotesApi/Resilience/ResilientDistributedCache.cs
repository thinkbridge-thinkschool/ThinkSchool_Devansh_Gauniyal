using Microsoft.Extensions.Caching.Distributed;
using Polly;

namespace QuotesApi.Resilience;

// Decorates the real Redis-backed IDistributedCache (registered by
// AddStackExchangeRedisCache in Program.cs) with the Redis resilience pipeline and
// the fault-injection switch, and is registered AS IDistributedCache in its place -
// so every call HybridCache makes to its L2 (it only ever calls the async methods)
// transparently goes through both, with no change to Day 21's HybridCache/caching
// code at all.
//
// The sync Get/Set/Remove/Refresh methods below are NOT wrapped: HybridCache is
// fully async and never calls them, and IDistributedCache still requires them to be
// implemented. Delegating them straight to the inner cache (undecorated) rather than
// blocking on the async, resilience-wrapped versions avoids a misleading appearance
// of protection on a path nothing in this app actually uses.
public sealed class ResilientDistributedCache(
    IDistributedCache inner,
    ResiliencePipeline pipeline,
    FaultInjectionSwitch faultSwitch) : IDistributedCache
{
    public byte[]? Get(string key) => inner.Get(key);
    public void Refresh(string key) => inner.Refresh(key);
    public void Remove(string key) => inner.Remove(key);
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        inner.Set(key, value, options);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        pipeline.ExecuteAsync(
            async ct =>
            {
                await faultSwitch.MaybeInjectAsync(ct);
                return await inner.GetAsync(key, ct);
            },
            token).AsTask();

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        pipeline.ExecuteAsync(
            async ct =>
            {
                await faultSwitch.MaybeInjectAsync(ct);
                await inner.RefreshAsync(key, ct);
                return true; // ExecuteAsync<T> needs a result; discarded by the Task-returning wrapper below.
            },
            token).AsTask();

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        pipeline.ExecuteAsync(
            async ct =>
            {
                await faultSwitch.MaybeInjectAsync(ct);
                await inner.RemoveAsync(key, ct);
                return true;
            },
            token).AsTask();

    public Task SetAsync(
        string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        pipeline.ExecuteAsync(
            async ct =>
            {
                await faultSwitch.MaybeInjectAsync(ct);
                await inner.SetAsync(key, value, options, ct);
                return true;
            },
            token).AsTask();
}
