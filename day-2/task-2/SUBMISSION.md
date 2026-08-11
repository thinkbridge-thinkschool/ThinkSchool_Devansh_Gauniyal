# Thinkbridge Submission Pack: Day 2, Task 2

## Task requirement

Cancellation must flow through the complete Collection request path:

```text
HTTP request -> endpoint/controller -> service -> repository -> EF Core
```

Every asynchronous method that performs I/O accepts `CancellationToken` as its final parameter. The integration test must call a real Collection HTTP endpoint, wait until the underlying operation has started, cancel mid-request, and prove the operation did not complete.

## Actual endpoint

`POST /api/collections` receives ASP.NET Core's request-aborted token and passes it to the service:

```csharp
[ApiController]
[Route("api/collections")]
public sealed class CollectionsController(ICollectionService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CollectionResponse>> Create(
        CreateCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await service.CreateAsync(
                request.Name,
                request.OwnerId,
                cancellationToken);
            return Created($"/api/collections/{collection.Id}", ToResponse(collection));
        }
        catch (CollectionInvariantException exception)
        {
            return InvariantProblem(exception);
        }
    }
}
```

The add-item and remove-item actions also accept `CancellationToken` last and pass it to their matching service methods.

## Complete cancellation-aware service interface

```csharp
using CollectionApi.Models;

namespace CollectionApi.Services;

public interface ICollectionService
{
    Task<Collection> CreateAsync(
        string? name,
        int ownerId,
        CancellationToken cancellationToken);

    Task<Collection?> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);

    Task<bool> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);
}
```

## Complete cancellation-aware service implementation

```csharp
using CollectionApi.Models;
using CollectionApi.Repositories;
using CollectionApi.Services.Time;

namespace CollectionApi.Services;

public sealed class CollectionService(
    ICollectionRepository repository,
    IClock clock) : ICollectionService
{
    public async Task<Collection> CreateAsync(
        string? name,
        int ownerId,
        CancellationToken cancellationToken)
    {
        var collection = new Collection(name, ownerId);
        await repository.AddAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<Collection?> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await repository.GetByIdAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        collection.AddItem(quoteId, clock.UtcNow);
        await repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<bool> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await repository.GetByIdAsync(collectionId, cancellationToken);
        if (collection is null || !collection.RemoveItem(quoteId))
        {
            return false;
        }

        await repository.UpdateAsync(collection, cancellationToken);
        return true;
    }
}
```

## Repository interface

```csharp
using CollectionApi.Models;

namespace CollectionApi.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(Collection collection, CancellationToken cancellationToken);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken);
    Task DeleteAsync(Collection collection, CancellationToken cancellationToken);
}
```

## Repository implementation and EF Core propagation

```csharp
using CollectionApi.Data;
using CollectionApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Repositories;

public sealed class CollectionRepository(CollectionDbContext dbContext)
    : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Collections
            .Include(collection => collection.Items)
            .SingleOrDefaultAsync(collection => collection.Id == id, cancellationToken);

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken)
    {
        await dbContext.Collections.AddAsync(collection, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken)
    {
        dbContext.Collections.Update(collection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Collection collection, CancellationToken cancellationToken)
    {
        dbContext.Collections.Remove(collection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

## Complete integration test

```csharp
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
```

## Complete test-only blocking repository

```csharp
using CollectionApi.Models;
using CollectionApi.Repositories;

namespace CollectionApi.Tests.Fakes;

public sealed class BlockingCollectionRepository : ICollectionRepository
{
    private readonly TaskCompletionSource _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancellationObserved = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;
    public Task CancellationObserved => _cancellationObserved.Task;
    public bool ReceivedCancellableToken { get; private set; }
    public bool OperationCompleted { get; private set; }

    public Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This test double only supports AddAsync.");

    public async Task AddAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        ReceivedCancellableToken = cancellationToken.CanBeCanceled;
        _started.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(15));
            OperationCompleted = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cancellationObserved.TrySetResult();
            throw;
        }
    }

    public Task UpdateAsync(
        Collection collection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This test double only supports AddAsync.");

    public Task DeleteAsync(
        Collection collection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This test double only supports AddAsync.");
}
```

The test replaces only the repository registration. The real `POST /api/collections` endpoint and real `CollectionService` process the request. The repository start signal guarantees cancellation happens while the operation is in progress rather than before it begins.

## Actual verification

- Restore: succeeded.
- Build: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Full suite: `Total tests: 7. Passed: 7.`
- Focused test: passed three additional consecutive runs (472 ms, 450 ms, and 458 ms).
- API startup: succeeded with no dependency-injection or startup errors.
- Existing success behavior: create 201, add item 200, remove item 204.
- API shutdown: clean, exit code 0.
- Async audit: no `.Result`, `.Wait()`, or `Task.Run` in the affected runtime path.
- Scope audit: no Day 1 or `day-2/task-1` file was changed.

## Git and GitHub

- Branch: `day-2/task-2`
- Implementation commit: `2a704fed710555335474d7b070702d06fd0bee13`
- Pull-request creation URL: https://github.com/devansh-gauniyal/thinkschool/pull/new/day-2/task-2
- Final folder link: https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-2
