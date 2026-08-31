using BackgroundJobsDemo.Queue;

namespace BackgroundJobsDemo.Worker;

public sealed class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QueuedHostedService starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, Task> workItem;
            try
            {
                workItem = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown arrived while waiting for the next item. Normal stop, not an error;
                // anything still sitting in the queue is deliberately abandoned -- see README,
                // "Graceful shutdown: what happens to queued work".
                break;
            }

            try
            {
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown arrived while this item was running. Also a normal stop: this demo
                // abandons the in-flight item rather than guaranteeing it finishes -- see README.
                break;
            }
            catch (Exception ex)
            {
                // A single bad job must never take the drain loop down with it.
                logger.LogError(ex, "Background work item threw and was skipped.");
            }
        }

        logger.LogInformation("QueuedHostedService stopping.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("QueuedHostedService stop requested.");
        await base.StopAsync(cancellationToken);
        logger.LogInformation("QueuedHostedService stopped.");
    }
}
