# Day 2, Task 2: Async/Await with Cancellation Through Layers

## What this task builds

This task extends the existing Collection API so a cancelled HTTP request can stop work all the way through the application:

```text
HTTP request -> CollectionsController -> CollectionService -> CollectionRepository -> EF Core
```

Each asynchronous method that performs I/O accepts `CancellationToken` as its final parameter and passes the same token to the next layer.

## Asynchronous I/O

I/O means waiting for something outside the current CPU work, such as a database query or an HTTP response. An asynchronous API releases the request thread while that operation is waiting. When the I/O finishes, execution resumes after `await`.

`Task.Run` is not useful for ordinary asynchronous I/O. It adds scheduling and allocation without making the database or network operation more asynchronous. The correct approach is to call the asynchronous API directly and await it.

## What a cancellation token represents

A `CancellationToken` is a cooperative signal that the caller no longer needs an operation. It does not forcibly terminate code. Each layer must observe the token itself or pass it to a dependency that does.

ASP.NET Core binds a controller action's `CancellationToken` parameter to `HttpContext.RequestAborted`. When the client cancels or disconnects, that token is cancelled. The controller passes it to `CollectionService`, which passes it to `ICollectionRepository`, which passes it to EF Core calls such as `SingleOrDefaultAsync` and `SaveChangesAsync`.

If one layer drops the token, work below that point may continue after the client has gone away. That wastes database capacity and can allow an operation the caller believed was cancelled to complete.

## Why EF Core needs the token

EF Core cannot react to request cancellation unless its asynchronous methods receive the token. In this project:

- `SingleOrDefaultAsync(..., cancellationToken)` can cancel a database query.
- `AddAsync(..., cancellationToken)` can cancel asynchronous value generation when a provider needs it.
- `SaveChangesAsync(cancellationToken)` can cancel the database save.

The synchronous `Update` and `Remove` calls only change EF Core's in-memory tracking state, so they do not need a token. Their subsequent database save does.

## Async mistakes avoided

- `.Result` and `.Wait()` synchronously block a thread and can cause thread starvation or deadlocks. This path uses `await`.
- `Task.Run` is not used around database work.
- No available caller token is replaced with `CancellationToken.None`.
- `OperationCanceledException` is not converted into a 500 response.
- Application methods that return `Task` perform real asynchronous work.

ASP.NET Core application code generally does not need `ConfigureAwait(false)` because it does not use the classic ASP.NET synchronization context.

## Why cancellation is usually not an HTTP response

When the client cancels its own request, there may no longer be a client waiting to receive a response. `HttpClient` therefore normally observes `OperationCanceledException` or its `TaskCanceledException` subclass instead of a conventional HTTP status. Some systems deliberately translate cancellation to non-standard status 499, but this API does not add special 499 middleware merely for the exercise.

## How the integration test proves propagation

`CollectionRequest_CancelledMidRequest_DoesNotCompleteOperation` sends a real `POST /api/collections` request through `WebApplicationFactory<Program>`. The test host replaces only `ICollectionRepository` with `BlockingCollectionRepository`; the real controller and real service remain active.

The blocking repository signals when `AddAsync` has actually started, records whether the received token can be cancelled, and waits using that token. Only after the start signal does the test cancel the HTTP request token. The assertions prove that:

- a cancellable token reached the repository;
- the client request ended with cancellation;
- the repository observed cancellation; and
- the repository operation never reached completion.

Five-second waits protect the test from hanging if propagation breaks. The repository also has a separate 15-second safety timeout for cleanup in a broken scenario. The test does not depend on a random sleep.

## Important files

- `CollectionApi/Controllers/CollectionsController.cs`: accepts the request-aborted token and passes it to the service.
- `CollectionApi/Services/ICollectionService.cs`: cancellation-aware service contract.
- `CollectionApi/Services/CollectionService.cs`: passes the token to every repository operation.
- `CollectionApi/Repositories/ICollectionRepository.cs`: cancellation-aware persistence contract.
- `CollectionApi/Repositories/CollectionRepository.cs`: passes the token to EF Core.
- `Tests/CollectionCancellationTests.cs`: real HTTP mid-request cancellation test.
- `Tests/Fakes/BlockingCollectionRepository.cs`: deterministic test-only blocking dependency.
- `SUBMISSION.md`: complete technical evidence.
- `MENTOR-NOTES.txt`: text ready for the Thinkbridge mentor-notes field.
- `FORM-ANSWERS.md`: all form fields ready to paste.

## Commands

Run from `day-2/task-2`:

```bash
dotnet restore Task2.slnx
dotnet build Task2.slnx --no-restore
dotnet test Task2.slnx --no-build --no-restore
dotnet test Tests/CollectionApi.Tests.csproj \
  --no-build --no-restore \
  --filter FullyQualifiedName~CollectionRequest_CancelledMidRequest_DoesNotCompleteOperation
dotnet run --no-build --project CollectionApi/CollectionApi.csproj
```

The default database is `collections.db`. Local SQLite database, WAL, build, test-result, and editor-user artifacts are ignored.

## Actual verified results

Verified on August 11, 2026 with .NET SDK 10.0.302:

- Restore succeeded.
- Build succeeded with 0 warnings and 0 errors.
- Full suite: 7 passed, 0 failed, 0 skipped.
- Focused cancellation test: passed three additional consecutive runs.
- API startup: succeeded with no dependency-injection or startup errors.
- Normal behavior: create returned 201, add item returned 200, and remove item returned 204.
- The smoke-test database was created outside the repository in a temporary directory.
- API shutdown completed cleanly with exit code 0.
