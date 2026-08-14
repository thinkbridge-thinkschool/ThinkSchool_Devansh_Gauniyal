using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Xunit;

namespace ResilienceDemo.Tests;

public class ResiliencePipelineTests
{
    // Builds a client wired to the exact same resilience pipeline production uses
    // (ResiliencePipelineConfiguration.Configure), with only the primary handler
    // replaced by a fake -- no Docker, no real network, no bound port.
    private static (RemoteService Service, ScriptedHandler Handler, CapturingLoggerProvider Logs) CreateProductionPipelineClient(
        ScriptedHandler handler)
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services
            .AddHttpClient(HttpClientNames.RemoteService, client => client.BaseAddress = new Uri("https://example.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler(ResiliencePipelineConfiguration.PipelineName, ResiliencePipelineConfiguration.Configure);

        var provider = services.BuildServiceProvider();
        var service = new RemoteService(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<RemoteService>>());

        return (service, handler, logs);
    }

    // Test-only pipeline isolating JUST the timeout strategy (no retry, no circuit
    // breaker), with a much shorter timeout than production's real 10 seconds --
    // otherwise this test would have to genuinely wait 10+ seconds to prove
    // anything. Production's actual timeout value is untouched; this exists only so
    // the test finishes in milliseconds instead of minutes.
    private static (HttpClient Client, ScriptedHandler Handler) CreateTimeoutOnlyClient(
        ScriptedHandler handler, TimeSpan timeout)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHttpClient("timeout-test", client => client.BaseAddress = new Uri("https://example.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("timeout-test", (builder, _) => builder.AddTimeout(timeout));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("timeout-test");
        return (client, handler);
    }

    // Test-only pipeline isolating JUST the circuit breaker (no retry, so each
    // outer call maps to exactly one handler invocation, making the invocation
    // count directly interpretable), with a much shorter sampling window and lower
    // minimum throughput than production's real 30 seconds / 10 calls -- otherwise
    // this test would have to genuinely drive 10 real failures inside a real
    // 30-second window. Production's actual circuit breaker configuration
    // (FailureRatio, SamplingDuration, MinimumThroughput) is untouched in
    // ResiliencePipelineConfiguration; this is a separate, smaller pipeline that
    // exists only to prove the circuit-opens-then-fails-fast *mechanism* works.
    private static (HttpClient Client, ScriptedHandler Handler) CreateCircuitBreakerOnlyClient(ScriptedHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHttpClient("circuit-test", client => client.BaseAddress = new Uri("https://example.invalid"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("circuit-test", (builder, _) =>
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(2),
                    MinimumThroughput = 4,
                    BreakDuration = TimeSpan.FromSeconds(30)
                }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("circuit-test");
        return (client, handler);
    }

    [Fact]
    public async Task TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var handler = new ScriptedHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        var (service, scriptedHandler, _) = CreateProductionPipelineClient(handler);

        var result = await service.GetDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        // The count, not just the successful result, is what proves retries
        // actually happened -- a pipeline with no retry at all would just fail on
        // the first 503 and never reach 3 invocations.
        Assert.Equal(3, scriptedHandler.InvocationCount);
    }

    [Fact]
    public async Task RetryAttempts_AreLogged_WithAttemptNumberAndReason()
    {
        var handler = new ScriptedHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        var (service, _, logs) = CreateProductionPipelineClient(handler);

        await service.GetDataAsync(CancellationToken.None);

        var retryLogs = logs.Entries.Where(e => e.State.ContainsKey("AttemptNumber")).ToList();

        // This test fails for a meaningful reason if logging is removed: with no
        // OnRetry callback at all, retryLogs would be empty.
        Assert.Equal(2, retryLogs.Count);
        Assert.Contains(retryLogs, e => Equals(e.State["AttemptNumber"], 1) && Equals(e.State["Reason"], "HTTP 503"));
        Assert.Contains(retryLogs, e => Equals(e.State["AttemptNumber"], 2) && Equals(e.State["Reason"], "HTTP 503"));
    }

    [Fact]
    public async Task AllRetriesExhausted_SurfacesAsFailure_NotSilentSuccess()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (service, scriptedHandler, logs) = CreateProductionPipelineClient(handler);

        // A genuine failure must propagate as an exception -- if RemoteService ever
        // "handled" this by returning an empty string instead, this assertion
        // fails.
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetDataAsync(CancellationToken.None));

        // Real, observed number: MaxRetryAttempts = 3, so 1 initial attempt + 3
        // retries = 4 total invocations.
        Assert.Equal(4, scriptedHandler.InvocationCount);

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("All attempts"));
    }

    [Fact]
    public async Task SlowDependency_TimesOut_RatherThanHanging()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.OK, delay: TimeSpan.FromMilliseconds(500));
        var (client, _) = CreateTimeoutOnlyClient(handler, timeout: TimeSpan.FromMilliseconds(100));

        // If timeout were removed from the pipeline, this would just wait out the
        // full 500ms delay and succeed -- the assertion below would fail because no
        // exception would be thrown, proving the timeout strategy is what's being
        // tested, not merely that the call eventually returns something.
        var exception = await Record.ExceptionAsync(() => client.GetAsync("/data"));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task RepeatedFailures_OpenCircuit_ThenFailFastWithoutInvokingHandler()
    {
        var handler = ScriptedHandler.AlwaysReturn(HttpStatusCode.ServiceUnavailable);
        var (client, scriptedHandler) = CreateCircuitBreakerOnlyClient(handler);

        // Drive enough failing calls to exceed MinimumThroughput (4) at a 50%
        // failure ratio -- all of these fail, so the circuit should open at or
        // before the 4th call.
        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/data");
        }

        var invocationsBeforeOpenCircuitCall = scriptedHandler.InvocationCount;

        // The circuit is now open. This call must fail fast -- BrokenCircuitException
        // -- WITHOUT the handler being invoked again. If the circuit breaker were
        // removed, this would just be another ordinary call to the handler, and the
        // invocation count below would have grown.
        await Assert.ThrowsAsync<Polly.CircuitBreaker.BrokenCircuitException>(
            () => client.GetAsync("/data"));

        Assert.Equal(invocationsBeforeOpenCircuitCall, scriptedHandler.InvocationCount);
    }

    [Fact]
    public async Task SuccessOnFirstAttempt_DoesNotRetry_AndLogsNoRetries()
    {
        var handler = new ScriptedHandler([HttpStatusCode.OK]);
        var (service, scriptedHandler, logs) = CreateProductionPipelineClient(handler);

        var result = await service.GetDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        // This is the test that catches retries firing when they shouldn't: if the
        // retry predicate were ever misconfigured to treat a 200 as retryable, both
        // assertions below would fail.
        Assert.Equal(1, scriptedHandler.InvocationCount);
        Assert.DoesNotContain(logs.Entries, e => e.State.ContainsKey("AttemptNumber"));
    }
}
