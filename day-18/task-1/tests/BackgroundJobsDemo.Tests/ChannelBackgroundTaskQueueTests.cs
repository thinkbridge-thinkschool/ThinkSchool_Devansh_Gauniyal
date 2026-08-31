using BackgroundJobsDemo.Queue;

namespace BackgroundJobsDemo.Tests;

public class ChannelBackgroundTaskQueueTests
{
    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelBackgroundTaskQueue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelBackgroundTaskQueue(-1));
    }

    [Fact]
    public async Task EnqueueAsync_NullWorkItem_ThrowsArgumentNullException()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(() => queue.EnqueueAsync(null!).AsTask());
    }

    [Fact]
    public async Task DequeueAsync_ReturnsItemsInFifoOrder()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 5);
        var order = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            var captured = i;
            await queue.EnqueueAsync(_ =>
            {
                order.Add(captured);
                return Task.CompletedTask;
            });
        }

        for (var i = 0; i < 3; i++)
        {
            var workItem = await queue.DequeueAsync(CancellationToken.None);
            await workItem(CancellationToken.None);
        }

        Assert.Equal([0, 1, 2], order);
    }

    [Fact]
    public async Task EnqueueAsync_AtCapacity_AppliesBackpressureUntilSpaceFrees()
    {
        var queue = new ChannelBackgroundTaskQueue(capacity: 2);
        await queue.EnqueueAsync(_ => Task.CompletedTask);
        await queue.EnqueueAsync(_ => Task.CompletedTask);

        // The queue is now at capacity. A third enqueue must genuinely wait for space -- a full
        // bounded channel with FullMode.Wait cannot complete this synchronously, so there is no
        // race to guess at here.
        var thirdEnqueue = queue.EnqueueAsync(_ => Task.CompletedTask).AsTask();
        Assert.False(thirdEnqueue.IsCompleted);

        // Freeing one slot lets the pending enqueue proceed.
        await queue.DequeueAsync(CancellationToken.None);
        await thirdEnqueue.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(thirdEnqueue.IsCompleted);
    }
}
