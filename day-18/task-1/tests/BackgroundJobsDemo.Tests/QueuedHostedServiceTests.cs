using System.Diagnostics;
using BackgroundJobsDemo.Queue;
using BackgroundJobsDemo.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackgroundJobsDemo.Tests;

public class QueuedHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesWorkItemsInFifoOrder()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 5);
        var service = new QueuedHostedService(queue, NullLogger<QueuedHostedService>.Instance);

        var order = new List<int>();
        var allDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < 3; i++)
        {
            var captured = i;
            await queue.EnqueueAsync(_ =>
            {
                order.Add(captured);
                if (captured == 2)
                {
                    allDone.SetResult();
                }

                return Task.CompletedTask;
            });
        }

        await service.StartAsync(CancellationToken.None);
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([0, 1, 2], order);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingWorkItem_IsLoggedAndDoesNotStopTheLoop()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 5);
        var logger = new CapturingLogger<QueuedHostedService>();
        var service = new QueuedHostedService(queue, logger);

        var secondItemRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await queue.EnqueueAsync(_ => throw new InvalidOperationException("boom"));
        await queue.EnqueueAsync(_ =>
        {
            secondItemRan.SetResult();
            return Task.CompletedTask;
        });

        await service.StartAsync(CancellationToken.None);

        // The real assertion: the second item still ran after the first one threw.
        await secondItemRan.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task StopAsync_CancelsTheLoopPromptly()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 5);
        var service = new QueuedHostedService(queue, NullLogger<QueuedHostedService>.Instance);

        // Nothing enqueued: the loop is parked awaiting the next item when we stop it.
        await service.StartAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"StopAsync took {stopwatch.Elapsed}, expected the parked read to cancel promptly.");
    }

    [Fact]
    public async Task Shutdown_AbandonsTheInFlightItemAndNeverStartsStillQueuedItems()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 5);
        var service = new QueuedHostedService(queue, NullLogger<QueuedHostedService>.Instance);

        var firstItemStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstItemObservedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondItemStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await queue.EnqueueAsync(async ct =>
        {
            firstItemStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                firstItemObservedCancellation.SetResult();
                throw;
            }
        });

        await queue.EnqueueAsync(_ =>
        {
            secondItemStarted.SetResult();
            return Task.CompletedTask;
        });

        await service.StartAsync(CancellationToken.None);
        await firstItemStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        // Documents the chosen shutdown behaviour for real: the in-flight item was interrupted
        // (not left to finish), and the still-queued second item was never dequeued at all.
        await firstItemObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(secondItemStarted.Task.IsCompleted);
    }
}
