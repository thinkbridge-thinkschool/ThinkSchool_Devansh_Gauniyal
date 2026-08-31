namespace BackgroundJobsDemo.Queue;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<CancellationToken, Task> workItem, CancellationToken cancellationToken = default);

    ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}
