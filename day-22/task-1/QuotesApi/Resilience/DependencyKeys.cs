namespace QuotesApi.Resilience;

// Keyed-DI keys and HttpClient name, kept as constants so registration, resolution,
// endpoint routes, and tests can never drift apart silently - the same discipline
// already used for HttpClientNames in day-5/task-6/ResilienceDemo.
public static class DependencyKeys
{
    public const string Redis = "redis";
    public const string ExternalService = "external-service";
}
