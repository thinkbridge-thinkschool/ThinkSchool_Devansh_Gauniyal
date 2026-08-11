using System.Net.Http.Json;
using CollectionApi.Dtos;
using CollectionApi.Repositories;
using CollectionApi.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CollectionApi.Tests;

public sealed class CollectionCancellationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CollectionRequest_CancelledMidRequest_DoesNotCompleteOperation()
    {
        var repository = new BlockingCollectionRepository();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:Collections",
                    "Data Source=:memory:");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICollectionRepository>();
                    services.AddSingleton(repository);
                    services.AddSingleton<ICollectionRepository>(repository);
                });
            });
        using var client = factory.CreateClient();
        using var requestCancellation = new CancellationTokenSource();

        var requestTask = client.PostAsJsonAsync(
            "/api/collections",
            new CreateCollectionRequest("Cancellation proof", OwnerId: 7),
            requestCancellation.Token);

        await repository.Started.WaitAsync(TestTimeout);
        Assert.True(repository.ReceivedCancellableToken);

        requestCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await requestTask.WaitAsync(TestTimeout));
        await repository.CancellationObserved.WaitAsync(TestTimeout);
        Assert.False(repository.OperationCompleted);
    }
}
