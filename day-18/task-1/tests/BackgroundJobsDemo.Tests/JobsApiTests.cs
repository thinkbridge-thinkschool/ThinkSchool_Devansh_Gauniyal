using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BackgroundJobsDemo.Tests;

public class JobsApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PostJobs_ReturnsAcceptedWellBeforeTheSimulatedJobWouldFinish()
    {
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsync("/api/jobs", content: null);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(150),
            $"POST took {stopwatch.Elapsed}; it should return before the 300ms simulated job " +
            "even starts, since enqueueing happens on the request thread but running does not.");
    }

    [Fact]
    public async Task GetJob_TransitionsFromQueuedToCompleted()
    {
        using var client = factory.CreateClient();

        var postResponse = await client.PostAsync("/api/jobs", content: null);
        var created = await postResponse.Content.ReadFromJsonAsync<JobIdResponse>();
        Assert.NotNull(created);

        JobRecordResponse? final = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var getResponse = await client.GetAsync($"/api/jobs/{created!.Id}");
            getResponse.EnsureSuccessStatusCode();
            final = await getResponse.Content.ReadFromJsonAsync<JobRecordResponse>();
            if (final!.Status == "Completed")
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.NotNull(final);
        Assert.Equal("Completed", final!.Status);
    }

    [Fact]
    public async Task GetJob_UnknownId_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record JobIdResponse(Guid Id);

    private sealed record JobRecordResponse(Guid Id, string Status, string? Error);
}
