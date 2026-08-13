using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Serilog.Events;

namespace QuotesApi.Tracing.Tests;

public sealed class TracingTests
{
    [Fact]
    public async Task RefreshTokenRotate_CreatesChildSpan_WithExpectedTagsAndParent()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = new TracingApiFactory();
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });
        loginResponse.EnsureSuccessStatusCode();
        var pair = await loginResponse.Content.ReadFromJsonAsync<TokenPairDto>();

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refresh_token = pair!.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();

        var rotateActivity = Assert.Single(
            activities, a => a.DisplayName == "refresh-token.rotate");

        // Would fail if the custom span were removed, or moved somewhere it no longer
        // has a parent HTTP request activity to nest under.
        Assert.NotNull(rotateActivity.ParentId);
        Assert.Equal("rotated", rotateActivity.GetTagItem("refresh_token.outcome"));
        Assert.NotNull(rotateActivity.GetTagItem("user.id"));
    }

    [Fact]
    public async Task LoginRequest_LogLineAndTraceShareTheSameTraceId()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = new TracingApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });
        response.EnsureSuccessStatusCode();

        var logTraceId = factory.Sink.Events
            .Select(GetLogTraceId)
            .FirstOrDefault(id => id is not null);
        Assert.NotNull(logTraceId);

        var activityTraceId = Assert.Single(
            activities.Select(a => a.TraceId.ToString()).Distinct());

        // This is the actual point of Step 4(b)'s fix: without it, logTraceId would be
        // ASP.NET Core's ctx.TraceIdentifier (a different format entirely) and this
        // assertion would fail every time, not just occasionally.
        Assert.Equal(activityTraceId, logTraceId);
    }

    private static string? GetLogTraceId(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("TraceId", out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed record TokenPairDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
