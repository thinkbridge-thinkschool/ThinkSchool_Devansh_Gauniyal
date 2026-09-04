using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using System.Threading.RateLimiting;

namespace QuotesApi.Resilience;

// Redis gets THREE strategies, deliberately not four: timeout, circuit breaker,
// bulkhead (concurrency limiter). No retry - see README.md "why retry is not on the
// Redis path": on a cache failure the correct move is to skip the cache and go
// straight to the source of truth (the database), not to retry the cache read.
// HybridCache's own GetOrCreateAsync already does exactly that fallback once this
// pipeline lets a failure through (see CountingPerformanceDbContext /
// AuthorQuoteSummaryReader from Day 21 - untouched).
//
// Built as a plain, non-generic ResiliencePipeline (not ResiliencePipeline<T>)
// because IDistributedCache's methods return different result types (byte[]?, and
// void-shaped writes) - the non-generic pipeline's ExecuteAsync<TResult> accepts any
// result type per call, verified against the installed Polly.Core 8.7.0 package
// (Polly.ResiliencePipeline.ExecuteAsync<T>(Func<CancellationToken,
// ValueTask<T>>, CancellationToken)) rather than assumed from a generic-only API.
public static class RedisResiliencePipelineConfiguration
{
    // Order, outermost to innermost: Bulkhead -> Circuit breaker -> Timeout.
    // - Bulkhead outermost: caps how many Redis calls this app has in flight at
    //   once, checked before anything else - a system-wide concurrency gate, not a
    //   per-call concern.
    // - Circuit breaker inside that: once open, a call fails immediately without
    //   even trying to acquire a bulkhead permit or start a timeout clock.
    // - Timeout innermost: bounds a single Redis call.
    // (No retry to reorder around here - see the type-level comment above.)
    //
    // `tuning` defaults to production values (ResilienceTuningOptions' own defaults)
    // but tests pass much shorter sampling/break/timeout windows - see
    // ResilienceTuningOptions.cs and ResilienceTests.cs - so the breaker lifecycle is
    // observable in well under a second instead of the real 5-15s this is tuned for.
    public static ResiliencePipeline Build(
        FaultInjectionSwitch faultSwitch,
        CircuitBreakerStateProvider stateProvider,
        ILogger logger,
        ResilienceTuningOptions? tuning = null)
    {
        tuning ??= new ResilienceTuningOptions();
        var builder = new ResiliencePipelineBuilder();

        // Bulkhead: at most PermitLimit concurrent Redis calls from this app; up to
        // QueueLimit more may queue waiting for a slot before being rejected. Redis
        // handles far higher real concurrency than the production defaults (8/4) -
        // deliberately low so a demo button can actually produce a rejection with a
        // modest N, not a realistic production limit.
        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = tuning.BulkheadPermitLimit,
                QueueLimit = tuning.BulkheadQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            },
            OnRejected = args =>
            {
                logger.LogWarning(
                    "Bulkhead REJECTED a Redis call - {PermitLimit} in flight already, queue full.",
                    tuning.BulkheadPermitLimit);
                return ValueTask.CompletedTask;
            }
        });

        // Circuit breaker: opens once FailureRatio of calls fail within
        // SamplingDuration, once at least MinimumThroughput calls have happened in
        // that window (so one bad call can't trip it). Production defaults (5s / 4,
        // vs. day-5/task-6's HTTP breaker at 30s / 10) are deliberate: this is a
        // local cache dependency on the same machine, exercised by a demo button, not
        // a production service seeing steady real traffic - chosen so the breaker
        // lifecycle is observable in seconds, not minutes. Every value re-explained
        // in README.md.
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = tuning.FailureRatio,
            SamplingDuration = tuning.SamplingDuration,
            MinimumThroughput = tuning.MinimumThroughput,
            BreakDuration = tuning.BreakDuration,
            StateProvider = stateProvider,
            OnOpened = args =>
            {
                logger.LogError(
                    "Circuit breaker OPENED for Redis for {BreakDuration}; further calls will " +
                    "fail fast and HybridCache will fall back to the database.",
                    args.BreakDuration);
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                logger.LogWarning(
                    "Circuit breaker for Redis is now HALF-OPEN; the next call is a probe.");
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                logger.LogInformation(
                    "Circuit breaker CLOSED for Redis; calls will reach Redis again.");
                return ValueTask.CompletedTask;
            }
        });

        // Timeout: bounds a single Redis call. Comfortably above real Redis latency
        // (sub-millisecond on localhost) but well below the fault-injection switch's
        // default 5-second "Slow" delay, so a Slow-mode call reliably times out
        // instead of racing to complete first.
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = tuning.Timeout,
            OnTimeout = args =>
            {
                logger.LogWarning(
                    "Timeout fired for a Redis call after {Timeout}.",
                    args.Timeout);
                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }
}
