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
