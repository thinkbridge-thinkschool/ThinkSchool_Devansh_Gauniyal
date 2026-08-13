using System.Net.Http.Json;
using Serilog.Events;

namespace QuotesApi.Logging.Tests;

public sealed class CorrelationTests
{
    [Fact]
    public async Task SingleRequest_LogsShareOneTraceId_AndDifferentRequestsGetDifferentIds()
    {
        using var factory = new LoggingApiFactory();
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });
        var secondResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var groupedByTraceId = factory.Sink.Events
            .Select(GetTraceId)
            .Where(id => id is not null)
            .Cast<string>()
            .GroupBy(id => id)
            .ToList();

        Assert.True(
            groupedByTraceId.Count >= 2,
            $"Expected at least 2 distinct TraceIds (one per request), found {groupedByTraceId.Count}.");

        foreach (var group in groupedByTraceId)
        {
            Assert.True(
                group.Count() >= 2,
                $"TraceId {group.Key} only had {group.Count()} log line(s); a request's log " +
                "lines should share one correlation ID across multiple log statements.");
        }
    }

    [Fact]
    public async Task SingleLoginRequest_ProducesAtLeastFiveLogLines()
    {
        using var factory = new LoggingApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });
        response.EnsureSuccessStatusCode();

        var traceIds = factory.Sink.Events
            .Select(GetTraceId)
            .Where(id => id is not null)
            .ToList();

        Assert.True(
            traceIds.Count >= 5,
            $"Expected at least 5 log lines carrying a TraceId for one login request, found {traceIds.Count}.");

        Assert.Single(traceIds.Distinct());
    }

    private static string? GetTraceId(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("TraceId", out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;
}
