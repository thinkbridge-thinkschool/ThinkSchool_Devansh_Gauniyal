namespace ResilienceDemo;

// The name the resilient HttpClient is registered under. Kept as a constant so the
// registration in Program.cs and the resolution here (and in tests) can never drift
// apart silently.
public static class HttpClientNames
{
    public const string RemoteService = "remote-service";
}

// Wraps the named, resilience-wrapped HttpClient behind a real call path. This is
// deliberately the only place that calls the remote service, so the resilience
// pipeline registered on the named client in Program.cs is genuinely exercised
// through normal use, not configuration nobody calls.
public class RemoteService(IHttpClientFactory httpClientFactory, ILogger<RemoteService> logger)
{
    public async Task<string> GetDataAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientNames.RemoteService);
        var response = await client.GetAsync("/data", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Polly's retry strategy itself has no separate "gave up" callback --
            // only OnRetry per attempt. This is the one place that knows the whole
            // pipeline (every retry, and the circuit breaker) has already done what
            // it could and the call still failed, so it's the right place to log
            // the terminal failure explicitly, once, before it's surfaced.
            logger.LogError(
                "All attempts for {ClientName} failed; final status {StatusCode}.",
                HttpClientNames.RemoteService,
                (int)response.StatusCode);
        }

        // This is the line that makes "never silently swallow failures" true.
        // Without it, a 503 that survives every retry would still return a
        // 200-shaped response object to the caller with whatever body came
        // attached, which looks like success. EnsureSuccessStatusCode() throws
        // HttpRequestException for any non-2xx status, so an exhausted-retries
        // failure genuinely propagates as a failure, not a fake success.
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
