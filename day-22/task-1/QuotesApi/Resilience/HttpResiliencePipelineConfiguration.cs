using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using System.Threading.RateLimiting;

namespace QuotesApi.Resilience;

// The HTTP dependency gets all FOUR strategies - this is the one operation in this
// task that's genuinely idempotent (a GET with no side effects), so it's the only
// one that gets retry. See README.md "why retry is idempotent-only".
public static class HttpResiliencePipelineConfiguration
{
    public const string PipelineName = "external-service";

    // Order, outermost to innermost: Bulkhead -> Retry -> Circuit breaker -> Timeout.
    //
    // This is a deliberate choice, not the order the brief happened to list the
    // strategies in - reasoned out from what each strategy needs to see:
    //
    // - Bulkhead OUTERMOST: it caps how many *logical* calls to this dependency are
    //   in flight across the whole app at once. If it were placed inside retry
    //   instead, each retry attempt of the same logical call would compete for a
    //   fresh permit, and the accounting would stop meaning "how many calls are
    //   outstanding" and start meaning "how many attempts are outstanding" - the
    //   wrong thing to limit. This also matches Microsoft's own AddStandardResilienceHandler
    //   in this same package (Microsoft.Extensions.Http.Resilience 10.9.0), whose
    //   documented default order places its rate limiter outermost, ahead of retry.
    // - Retry next: governs the whole attempt sequence for one logical call, with
    //   exponential backoff between attempts.
    // - Circuit breaker inside retry: every retry attempt passes through the breaker
    //   check first, so once the circuit opens, further retries fail fast instead of
    //   still trying - same reasoning day-5/task-6/ResilienceDemo already
    //   established for its 3-strategy pipeline.
    // - Timeout INNERMOST: applies per individual attempt, not the whole retry
    //   sequence - so one slow attempt can't silently consume the entire retry
    //   budget's time before a retry even gets a chance to run. Also matches
    //   day-5/task-6's reasoning.
    public static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> pipelineBuilder,
        ResilienceHandlerContext context)
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("QuotesApi.Resilience.ExternalService");
        var stateProvider = context.ServiceProvider
            .GetRequiredKeyedService<CircuitBreakerStateProvider>(DependencyKeys.ExternalService);

        // Read from configuration (same "ResilienceTuning" section as the Redis
        // pipeline) rather than a fixed value baked into this delegate, because
        // AddResilienceHandler's Configure signature is fixed by Polly - this is the
        // only way tests can shrink the sampling/break/timeout windows down from
        // production's 5-15s to well under a second. See ResilienceTuningOptions.cs.
        var tuning = context.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetSection(ResilienceTuningOptions.SectionName).Get<ResilienceTuningOptions>()
            ?? new ResilienceTuningOptions();

        // Bulkhead: at most PermitLimit concurrent calls; up to QueueLimit more may
        // queue before being rejected outright. Same reasoning on the numbers as the
        // Redis pipeline - production defaults (8/4) are low enough that a demo
        // button can actually trigger a rejection, not a realistic production limit.
        pipelineBuilder.AddRateLimiter(new RateLimiterStrategyOptions
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
                    "Bulkhead REJECTED a call to {ClientName} - too many concurrent calls already.",
                    DependencyKeys.ExternalService);
                return ValueTask.CompletedTask;
            }
        });

        // Retry: 3 attempts, exponential backoff, jittered - identical shape to
        // day-5/task-6's already-verified configuration. Jitter matters because if
        // many clients all back off on the exact same schedule after a shared
        // dependency blips, they all retry at the same instant and re-create the
        // spike that just failed. This is safe to retry BECAUSE the endpoint being
        // called (GET /api/external/quote-of-the-day) is idempotent - a GET with no
        // side effects; see README.md and ExternalServiceClient.cs for where that
        // guarantee is enforced (retry is configured only on this named client, and
        // only a GET is ever issued through it).
        pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = tuning.RetryMaxAttempts,
            Delay = tuning.RetryBaseDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = tuning.RetryUseJitter,
            OnRetry = args =>
            {
                var reason = args.Outcome.Exception is { } exception
                    ? exception.GetType().Name
                    : $"HTTP {(int)args.Outcome.Result!.StatusCode}";

                logger.LogWarning(
                    "Retry attempt {AttemptNumber} for {ClientName} after {Reason}; " +
                    "waiting {RetryDelay} before the next attempt.",
                    args.AttemptNumber + 1,
                    DependencyKeys.ExternalService,
                    reason,
                    args.RetryDelay);

                return ValueTask.CompletedTask;
            }
        });

        // Circuit breaker: opens once FailureRatio of calls fail within
        // SamplingDuration, once at least MinimumThroughput calls have happened in
        // that window. Production defaults (5s / 4) are deliberately shorter/lower
        // than day-5/task-6's 30s/10 (tuned for a demo you can watch happen in
        // seconds, not a production service under steady real traffic) - every value
        // re-explained in README.md.
        pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = tuning.FailureRatio,
            SamplingDuration = tuning.SamplingDuration,
            MinimumThroughput = tuning.MinimumThroughput,
            BreakDuration = tuning.BreakDuration,
            StateProvider = stateProvider,
            OnOpened = args =>
            {
                logger.LogError(
                    "Circuit breaker OPENED for {ClientName} for {BreakDuration}; further calls " +
                    "will fail fast instead of hitting the dependency.",
                    DependencyKeys.ExternalService,
                    args.BreakDuration);
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                logger.LogWarning(
                    "Circuit breaker for {ClientName} is now HALF-OPEN; the next call is a probe.",
                    DependencyKeys.ExternalService);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                logger.LogInformation(
                    "Circuit breaker CLOSED for {ClientName}; calls will reach the dependency again.",
                    DependencyKeys.ExternalService);
                return ValueTask.CompletedTask;
            }
        });

        // Timeout: bounds an individual attempt (innermost - see order comment
        // above), well below the fault switch's default 5-second Slow delay. Uses
        // the TimeoutStrategyOptions overload (not the plain-TimeSpan one) so a
        // timeout firing is logged explicitly - one of the required structured
        // log events (Phase 5.5), matching the Redis pipeline's own OnTimeout.
        pipelineBuilder.AddTimeout(new Polly.Timeout.TimeoutStrategyOptions
        {
            Timeout = tuning.Timeout,
            OnTimeout = args =>
            {
                logger.LogWarning(
                    "Timeout fired for a call to {ClientName} after {Timeout}.",
                    DependencyKeys.ExternalService,
                    args.Timeout);
                return ValueTask.CompletedTask;
            }
        });
    }
}
