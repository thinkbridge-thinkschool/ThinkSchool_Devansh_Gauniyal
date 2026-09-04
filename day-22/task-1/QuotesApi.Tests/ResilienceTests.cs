using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using QuotesApi.Resilience;

namespace QuotesApi.Tests;

// Two kinds of test here, deliberately:
//
// 1. Isolated pipeline tests build ONLY the strategy (or strategies) under test
//    against a ServiceCollection and a ScriptedHandler/CapturingLoggerProvider - the
//    exact pattern day-5/task-6/ResilienceDemo.Tests/ResiliencePipelineTests.cs
//    already established (copied helpers, see ScriptedHandler.cs /
//    CapturingLoggerProvider.cs). No web host, no real network, no Docker - fast and
//    precise, with real captured log state (AttemptNumber, RetryDelay, etc.), not
//    reconstructed strings. Breaker-lifecycle tests use a circuit-breaker-only
//    pipeline (CreateCircuitBreakerOnlyClient) rather than the full four-strategy
//    HttpResiliencePipelineConfiguration.Configure, for the same reason day-5/task-6
//    isolated theirs: Polly.Core 8.7.0 requires MaxRetryAttempts >= 1 (confirmed by a
//    real ValidationException, not assumed - see README.md's verification log), so
//    retry can never be fully switched off inside the real pipeline; testing the
//    breaker's own open/half-open/closed mechanics needs it isolated from retry noise
//    entirely, not merely minimized.
// 2. Full-app tests (ResilienceApiFactory) for things that only make sense with the
//    whole app wired together: Redis's own pipeline (which isn't HTTP-shaped),
//    graceful degradation to the database, and the fault-injection endpoints.
//
// Tuning windows are overridden to millisecond-scale via configuration
// (ResilienceTuningOptions) rather than Thread.Sleep-ing through production's real
// 5-15 second values - BreakDuration and SamplingDuration can't go below 500ms
// though: Polly.Core 8.7.0 validates both with a hard "must be greater than 0.5
// seconds" floor (confirmed by a real ValidationException, not assumed), so 600ms is
// the shortest BreakDuration used here. The break-duration tests wait out that real
// 600ms window (750ms, comfortably longer) - not a guess, the literal value passed to
// the pipeline each test builds.
public sealed class ResilienceTests
{
    private static (HttpClient Client, ScriptedHandler Handler, CapturingLoggerProvider Logs)
        CreateFullPipelineClient(ScriptedHandler handler, Dictionary<string, string?>? tuningOverrides = null)
    {
        var logs = new CapturingLoggerProvider();
        var stateProvider = new CircuitBreakerStateProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(tuningOverrides ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKeyedSingleton(DependencyKeys.ExternalService, stateProvider);
        services
            .AddHttpClient("test-client", client => client.BaseAddress = new Uri("https://example.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler(
                HttpResiliencePipelineConfiguration.PipelineName,
                HttpResiliencePipelineConfiguration.Configure);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test-client");
        return (client, handler, logs);
    }

    // Isolates JUST the circuit breaker (no retry, no timeout, no bulkhead) so the
    // open/half-open/closed mechanics are tested without anything else interfering -
    // same reasoning and shape as day-5/task-6's CreateCircuitBreakerOnlyClient, using
    // this task's own production breaker configuration values (via
    // ResilienceTuningOptions) rather than duplicating magic numbers.
    private static (HttpClient Client, CapturingLoggerProvider Logs, CircuitBreakerStateProvider StateProvider)
        CreateCircuitBreakerOnlyClient(ScriptedHandler handler, ResilienceTuningOptions tuning)
    {
        var logs = new CapturingLoggerProvider();
        var stateProvider = new CircuitBreakerStateProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services
            .AddHttpClient("breaker-only-test", client => client.BaseAddress = new Uri("https://example.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("breaker-only-test", (builder, context) =>
            {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("QuotesApi.Tests.BreakerOnly");
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = tuning.FailureRatio,
                    SamplingDuration = tuning.SamplingDuration,
                    MinimumThroughput = tuning.MinimumThroughput,
                    BreakDuration = tuning.BreakDuration,
                    StateProvider = stateProvider,
                    OnOpened = _ =>
                    {
                        logger.LogError("Circuit breaker OPENED (test-isolated pipeline).");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = _ =>
                    {
                        logger.LogWarning("Circuit breaker is now HALF-OPEN (test-isolated pipeline).");
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("Circuit breaker CLOSED (test-isolated pipeline).");
                        return ValueTask.CompletedTask;
                    }
                });
            });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("breaker-only-test");
        return (client, logs, stateProvider);
    }

    private static ResilienceTuningOptions FastBreakerTuning() => new()
    {
        FailureRatio = 0.5,
        SamplingDurationMs = 2000,
        MinimumThroughput = 4,
        BreakDurationMs = 600
    };

    // Fast tuning for the full four-strategy pipeline: short enough that the whole
    // retry+backoff sequence finishes in well under a second.
    private static Dictionary<string, string?> FastTuning(
        int? timeoutMs = null, int? bulkheadPermitLimit = null, int? bulkheadQueueLimit = null, bool useJitter = true) => new()
    {
        ["ResilienceTuning:SamplingDurationMs"] = "2000",
        ["ResilienceTuning:MinimumThroughput"] = "4",
        ["ResilienceTuning:BreakDurationMs"] = "600",
        ["ResilienceTuning:TimeoutMs"] = (timeoutMs ?? 150).ToString(),
        ["ResilienceTuning:RetryMaxAttempts"] = "3",
        ["ResilienceTuning:RetryBaseDelayMs"] = "20",
        ["ResilienceTuning:RetryUseJitter"] = useJitter.ToString(),
        ["ResilienceTuning:BulkheadPermitLimit"] = (bulkheadPermitLimit ?? 2).ToString(),
        ["ResilienceTuning:BulkheadQueueLimit"] = (bulkheadQueueLimit ?? 1).ToString()
    };

    [Fact]
    public async Task Retry_RetriesIdempotentCall_ConfiguredNumberOfTimes()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, scriptedHandler, _) = CreateFullPipelineClient(handler, FastTuning());

        using var response = await client.GetAsync("/data");

        // 1 initial attempt + 3 retries (RetryMaxAttempts = 3) = 4 real invocations.
        Assert.Equal(4, scriptedHandler.InvocationCount);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Retry_UsesIncreasingExponentialBackoffDelays()
    {
        // Jitter off for this one test: with jitter on (production's real setting,
        // exercised by every OTHER test in this file that uses FastTuning's default),
        // Polly's randomization can occasionally make one delay shorter than the
        // previous one - caught for real (not assumed) when this test first asserted
        // strict pairwise ordering with jitter on and failed with a genuine captured
        // pair (25.9ms then 30.8ms - and 30.8 > 25.9, so the *next* attempt was
        // shorter, not longer). See README.md's verification log. Jitter itself is
        // still proven live: the actual varying real delays captured manually while
        // exercising this app (6.6s, 1.28s, 1.5s, 6.2s across one real breaker-opening
        // sequence) are recorded in README.md/output/, not just asserted here.
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, _, logs) = CreateFullPipelineClient(handler, FastTuning(useJitter: false));

        using var response = await client.GetAsync("/data");

        var retryDelays = logs.Entries
            .Where(e => e.State.ContainsKey("RetryDelay"))
            .Select(e => (TimeSpan)e.State["RetryDelay"]!)
            .ToList();

        Assert.Equal(3, retryDelays.Count);
        Assert.True(retryDelays[1] > retryDelays[0], $"{retryDelays[1]} should exceed {retryDelays[0]}");
        Assert.True(retryDelays[2] > retryDelays[1], $"{retryDelays[2]} should exceed {retryDelays[1]}");
    }

    [Fact]
    public async Task Timeout_Fires_WhenDependencyIsSlow()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.OK, delay: TimeSpan.FromSeconds(2));
        var (client, _, logs) = CreateFullPipelineClient(
            handler,
            FastTuning(timeoutMs: 100, bulkheadPermitLimit: 10, bulkheadQueueLimit: 10));

        // With a 2s delay against a 100ms timeout, every attempt times out - the call
        // ultimately fails (retries exhausted against a dependency that never
        // responds in time), proving the timeout strategy is what's firing, not
        // merely that the call eventually returns something.
        var exception = await Record.ExceptionAsync(() => client.GetAsync("/data"));

        Assert.NotNull(exception);
        Assert.Contains(logs.Entries, e => e.Message.Contains("Timeout fired"));
    }

    [Fact]
    public async Task SustainedFailure_OpensBreaker()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, _, stateProvider) = CreateCircuitBreakerOnlyClient(handler, FastBreakerTuning());

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }

        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);
    }

    [Fact]
    public async Task OpenBreaker_ShortCircuits_WithoutInvokingTheDependencyAtAll()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, _, stateProvider) = CreateCircuitBreakerOnlyClient(handler, FastBreakerTuning());

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }
        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);
        var invocationsBeforeOpenCircuitCall = handler.InvocationCount;

        // The dependency must NOT be invoked again - if the breaker were removed,
        // InvocationCount below would have grown past invocationsBeforeOpenCircuitCall.
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("/data"));

        Assert.Equal(invocationsBeforeOpenCircuitCall, handler.InvocationCount);
    }

    [Fact]
    public async Task AfterBreakDuration_BreakerGoesHalfOpen()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, logs, stateProvider) = CreateCircuitBreakerOnlyClient(handler, FastBreakerTuning());

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }
        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);

        // The one genuinely unavoidable real wait in this file: proving a TTL-style
        // break duration elapsed requires letting real time pass. Deterministic
        // because it waits comfortably longer (750ms) than the literal 600ms
        // BreakDuration this test itself configured (FastBreakerTuning), not a guess
        // at production's real value.
        await Task.Delay(750);

        // This handler always returns 503, so the probe below also fails - this test
        // only proves the OPEN -> HALF-OPEN transition itself (via the log line);
        // the next two tests prove what happens after the probe succeeds or fails.
        using var probeResponse = await client.GetAsync("/data");

        Assert.Contains(logs.Entries, e => e.Message.Contains("HALF-OPEN"));
    }

    [Fact]
    public async Task SuccessfulProbe_ClosesBreaker()
    {
        // Fails exactly enough times to open the breaker, then succeeds - the probe
        // (the first call after the break duration) lands on that success.
        var handler = new ScriptedHandler(
        [
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK
        ]);
        var (client, logs, stateProvider) = CreateCircuitBreakerOnlyClient(handler, FastBreakerTuning());

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }
        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);

        await Task.Delay(750);

        using var probeResponse = await client.GetAsync("/data");

        Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);
        Assert.Equal(CircuitState.Closed, stateProvider.CircuitState);
        Assert.Contains(logs.Entries, e => e.Message.Contains("CLOSED"));
    }

    [Fact]
    public async Task FailedProbe_ReopensBreaker()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, _, stateProvider) = CreateCircuitBreakerOnlyClient(handler, FastBreakerTuning());

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }
        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);

        await Task.Delay(750);

        using var probeResponse = await client.GetAsync("/data");

        // The probe itself also failed (handler always returns 503), so the breaker
        // must go straight back to Open, not stay HalfOpen or incorrectly Close.
        Assert.Equal(CircuitState.Open, stateProvider.CircuitState);
    }

    [Fact]
    public async Task Bulkhead_RejectsWorkBeyondItsConcurrencyLimit()
    {
        // Slow enough that concurrent callers genuinely overlap in flight, and a
        // timeout wide enough that the timeout strategy doesn't fire first and mask
        // the bulkhead result. Every call succeeds (200 OK) so retry (still active,
        // Polly.Core requires MaxRetryAttempts >= 1) never triggers - it only
        // activates on a handled failure, which never happens here.
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.OK, delay: TimeSpan.FromMilliseconds(300));
        var (client, _, _) = CreateFullPipelineClient(
            handler,
            FastTuning(timeoutMs: 2000, bulkheadPermitLimit: 2, bulkheadQueueLimit: 1));

        // Permit=2, Queue=1 => 3 admitted, the rest rejected. Fire 6 concurrently.
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => client.GetAsync("/data")))
            .ToList();

        var results = await Task.WhenAll(tasks.Select(async t =>
        {
            try
            {
                using var response = await t;
                return "ok";
            }
            catch (RateLimiterRejectedException)
            {
                return "rejected";
            }
        }));

        Assert.Equal(3, results.Count(r => r == "rejected"));
        Assert.Equal(3, results.Count(r => r == "ok"));
    }

    // ===== Full-app tests: Redis's own pipeline, and graceful degradation =====

    private static async Task SetFaultModeAsync(HttpClient client, string dependency, string mode)
    {
        using var response = await client.PostAsync($"/api/faults/{dependency}?mode={mode}", content: null);
        response.EnsureSuccessStatusCode();
    }

    private sealed record CountResponse(int Count);

    private static async Task<int> GetDbQueryCountAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CountResponse>("/api/measurement/db-query-count"))!.Count;

    [Fact]
    public async Task Retry_IsNotAppliedToTheRedisPath()
    {
        using var factory = new ResilienceApiFactory(new Dictionary<string, string?>
        {
            ["ResilienceTuning:TimeoutMs"] = "5000" // wide, so this measures retry backoff, not a timeout race
        });
        using var client = factory.CreateClient();
        await SetFaultModeAsync(client, DependencyKeys.Redis, "failing");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.GetAsync("/api/resilience/redis/call");
        stopwatch.Stop();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", body.GetProperty("outcome").GetString());

        // No retry means one fault-injected failure returns near-instantly. If retry
        // were ever mistakenly added to the Redis pipeline (3 attempts with
        // exponential backoff starting at whatever RetryBaseDelayMs is), this would
        // take substantially longer - a generous 1s ceiling still easily separates
        // "no retry" from "any retry at all" without being a tight timing race.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Took {stopwatch.Elapsed} - looks retried.");
    }

    [Fact]
    public async Task RedisBreakerOpen_CachedEndpoint_StillServesCorrectDataFromTheDatabase()
    {
        using var factory = new ResilienceApiFactory(new Dictionary<string, string?>
        {
            ["ResilienceTuning:SamplingDurationMs"] = "2000",
            ["ResilienceTuning:MinimumThroughput"] = "4",
            ["ResilienceTuning:BreakDurationMs"] = "5000"
        });
        using var client = factory.CreateClient();

        await SetFaultModeAsync(client, DependencyKeys.Redis, "failing");
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/api/resilience/redis/call");
        }

        using var breakersResponse = await client.GetAsync("/api/resilience/breakers");
        var breakers = await breakersResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Open", breakers.GetProperty("redis").GetString());

        await client.PostAsync("/api/measurement/reset", content: null);
        var key = $"degrade-{Guid.NewGuid():N}";
        using var cachedResponse = await client.GetAsync($"/api/authors/quote-summary/cached?key={key}");

        cachedResponse.EnsureSuccessStatusCode();
        // 51 = 1 authors query + 50 explicit Collection().Load() calls - the real
        // AuthorQuoteSummaryQuery shape from Day 21, proving this came from the
        // database (HybridCache's factory), not from a Redis call that should have
        // been impossible with the breaker open.
        Assert.Equal(51, await GetDbQueryCountAsync(client));
    }
}
