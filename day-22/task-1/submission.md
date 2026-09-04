# Day 22, Task 1 — Resilience with Polly

## Notes for mentor

### Resilience pipeline — Redis (timeout, circuit breaker, bulkhead — no retry)

```csharp
// Order, outermost to innermost: Bulkhead -> Circuit breaker -> Timeout
var builder = new ResiliencePipelineBuilder();

builder.AddRateLimiter(new RateLimiterStrategyOptions
{
    DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
    {
        PermitLimit = 8, QueueLimit = 4, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    },
    OnRejected = args => { logger.LogWarning("Bulkhead REJECTED a Redis call..."); return ValueTask.CompletedTask; }
});

builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 0.5, SamplingDuration = TimeSpan.FromSeconds(5),
    MinimumThroughput = 4, BreakDuration = TimeSpan.FromSeconds(10),
    StateProvider = stateProvider,
    OnOpened = args => { logger.LogError("Circuit breaker OPENED for Redis for {BreakDuration}..."); return ValueTask.CompletedTask; },
    OnHalfOpened = args => { logger.LogWarning("Circuit breaker for Redis is now HALF-OPEN..."); return ValueTask.CompletedTask; },
    OnClosed = args => { logger.LogInformation("Circuit breaker CLOSED for Redis..."); return ValueTask.CompletedTask; }
});

builder.AddTimeout(new TimeoutStrategyOptions
{
    Timeout = TimeSpan.FromSeconds(2),
    OnTimeout = args => { logger.LogWarning("Timeout fired for a Redis call after {Timeout}."); return ValueTask.CompletedTask; }
});
```

### Resilience pipeline — external service (bulkhead, retry, circuit breaker, timeout)

```csharp
// Order, outermost to innermost: Bulkhead -> Retry -> Circuit breaker -> Timeout
pipelineBuilder.AddRateLimiter(new RateLimiterStrategyOptions
{
    DefaultRateLimiterOptions = new ConcurrencyLimiterOptions { PermitLimit = 8, QueueLimit = 4 },
    OnRejected = args => { logger.LogWarning("Bulkhead REJECTED a call..."); return ValueTask.CompletedTask; }
});

pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
{
    MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true,
    OnRetry = args => { logger.LogWarning("Retry attempt {AttemptNumber}... waiting {RetryDelay}..."); return ValueTask.CompletedTask; }
});

pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
{
    FailureRatio = 0.5, SamplingDuration = TimeSpan.FromSeconds(5),
    MinimumThroughput = 4, BreakDuration = TimeSpan.FromSeconds(10),
    StateProvider = stateProvider,
    OnOpened = args => { logger.LogError("Circuit breaker OPENED for {ClientName}..."); return ValueTask.CompletedTask; },
    OnHalfOpened = args => { logger.LogWarning("Circuit breaker for {ClientName} is now HALF-OPEN..."); return ValueTask.CompletedTask; },
    OnClosed = args => { logger.LogInformation("Circuit breaker CLOSED for {ClientName}..."); return ValueTask.CompletedTask; }
});

pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
{
    Timeout = TimeSpan.FromSeconds(2),
    OnTimeout = args => { logger.LogWarning("Timeout fired for a call to {ClientName}..."); return ValueTask.CompletedTask; }
});
```

### Real captured logs — breaker opening, half-open, closing (both dependencies)

From `output/breaker-lifecycle.txt`, one unbroken real run:

```
[12:12:08 ERR] Circuit breaker OPENED for external-service for 00:00:10; further calls will fail fast instead of hitting the dependency.
[12:12:20 WRN] Circuit breaker for external-service is now HALF-OPEN; the next call is a probe.
[12:12:20 INF] Circuit breaker CLOSED for external-service; calls will reach the dependency again.
[12:12:20 ERR] Circuit breaker OPENED for Redis for 00:00:10; further calls will fail fast and HybridCache will fall back to the database.
[12:12:33 WRN] Circuit breaker for Redis is now HALF-OPEN; the next call is a probe.
[12:12:33 INF] Circuit breaker CLOSED for Redis; calls will reach Redis again.
```

Real retry backoff delays, from `output/retry-backoff.txt`:
```
[12:12:05 WRN] Retry attempt 1 for external-service after HTTP 503; waiting 00:00:00.8739813 before the next attempt.
[12:12:06 WRN] Retry attempt 2 for external-service after HTTP 503; waiting 00:00:01.4574137 before the next attempt.
[12:12:07 WRN] Retry attempt 3 for external-service after HTTP 503; waiting 00:00:00.8399667 before the next attempt.
```

Real timeout and bulkhead rejection, from `output/timeout.txt` and `output/bulkhead-rejection.txt`:
```
[12:48:23 WRN] Timeout fired for a call to external-service after 00:00:02.
Polly.Timeout.TimeoutRejectedException: The operation didn't complete within the allowed timeout of '00:00:02'.

[12:48:41 WRN] Bulkhead REJECTED a call to external-service - too many concurrent calls already.
(x8 — real tally from 20 concurrent calls: 8 bulkhead-rejected, 12 short-circuited once the breaker opened mid-burst)
```

### Scope notes

- Retry is on the external-service pipeline only, because it's the only idempotent call here (a GET with no side effects) — retrying a Redis cache read isn't the right move; on a Redis failure the correct behaviour is to skip the cache and read the database directly, which HybridCache already does.
- This is a copy-forward of `day-21/task-1` (itself copied from `day-3/task-3`); both `day-21/task-1` and `day-3/task-3` are byte-for-byte untouched, and all 28 of Day 21's tests — including the stampede-protection test — still pass unchanged (39/39 total with the 11 new resilience tests).
- day-5/task-6/ResilienceDemo (read in full first) covers 3 strategies against 1 dependency with no live fault-injection or demo page; this task adds a 4th strategy (bulkhead), a 2nd dependency (Redis, not HTTP-shaped), explicit half-open logging, and a real, tested, working graceful-degradation path.

## What did you learn this session?

Order genuinely changes behavior, not just style — and once a breaker opens, it can silently swallow evidence from a later, unrelated demo scenario if you're not careful about sequencing.

## What would break this?

Swapping the Redis registration back to the plain, undecorated client would silently remove the whole safety net — Redis failures would go straight back to hanging or throwing instead of degrading to the database.
