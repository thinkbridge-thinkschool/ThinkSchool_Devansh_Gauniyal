using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ResilienceDemo;

// Extracted so the test project can register the exact same resilience pipeline as
// production (just with a fake primary handler standing in for the real network),
// instead of maintaining a second, hand-copied configuration that could quietly
// drift from what actually ships.
public static class ResiliencePipelineConfiguration
{
    public const string PipelineName = "default";

    public static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> pipelineBuilder,
        ResilienceHandlerContext context)
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ResilienceDemo.Http");

        // Retry: 3 attempts, exponential backoff, jittered. Jitter matters because if
        // many clients all back off on the exact same schedule after a shared
        // dependency blips, they all retry at the exact same instant and re-create
        // the exact spike that just failed. Jitter spreads that out.
        pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = args =>
            {
                // "Log every retry" -- attempt number, reason, and the delay before
                // the next attempt, all in one structured entry.
                var reason = args.Outcome.Exception is { } exception
                    ? exception.GetType().Name
                    : $"HTTP {(int)args.Outcome.Result!.StatusCode}";

                logger.LogWarning(
                    "Retry attempt {AttemptNumber} for {ClientName} after {Reason}; " +
                    "waiting {RetryDelay} before the next attempt.",
                    args.AttemptNumber + 1,
                    HttpClientNames.RemoteService,
                    reason,
                    args.RetryDelay);

                return ValueTask.CompletedTask;
            }
        });

        // Circuit breaker: opens once 50% of requests fail within a 30 second
        // sampling window. MinimumThroughput is a required, separate setting -- the
        // ratio isn't evaluated at all until at least this many calls have happened
        // in the window, so a single failure can't trip the breaker on its own.
        pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = args =>
            {
                logger.LogError(
                    "Circuit breaker OPENED for {ClientName} for {BreakDuration}; " +
                    "further calls will fail fast instead of hitting the dependency.",
                    HttpClientNames.RemoteService,
                    args.BreakDuration);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                logger.LogInformation(
                    "Circuit breaker CLOSED for {ClientName}; calls will reach the dependency again.",
                    HttpClientNames.RemoteService);
                return ValueTask.CompletedTask;
            }
        });

        // Timeout: applies to each individual attempt (it's innermost, added last),
        // not the whole retry sequence.
        pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(10));
    }
}
