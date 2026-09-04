namespace QuotesApi.Resilience;

// Wraps the named, resilience-wrapped HttpClient (registered in Program.cs) behind a
// real call path - the only place that calls the controllable external-service
// endpoint, so the resilience pipeline is genuinely exercised through normal use.
// Mirrors day-5/task-6/ResilienceDemo's RemoteService.cs deliberately, going further
// with a bulkhead strategy and an in-process controllable dependency instead of a
// real external base address.
public sealed class ExternalServiceClient(
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalServiceClient> logger)
{
    public async Task<string> GetQuoteOfTheDayAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(DependencyKeys.ExternalService);
        var response = await client.GetAsync("/api/external/quote-of-the-day", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // As in day-5/task-6: the one place that knows the whole pipeline (every
            // retry, the breaker, the bulkhead) has already done what it could and
            // the call still failed, so it logs the terminal failure explicitly,
            // once, before EnsureCreated ensures it's surfaced rather than swallowed.
            logger.LogError(
                "All attempts for {ClientName} failed; final status {StatusCode}.",
                DependencyKeys.ExternalService,
                (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
