using ServiceBusDemo.Core;

namespace ServiceBusDemo.Tests;

public class IdempotencyStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"idempotency-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void TryMarkProcessed_FirstTime_ReturnsTrue()
    {
        using var store = new IdempotencyStore(_dbPath);

        var result = store.TryMarkProcessed("msg-1", "worker-1");

        Assert.True(result);
    }

    [Fact]
    public void TryMarkProcessed_SameMessageIdTwice_OnlyFirstCallReturnsTrue()
    {
        using var store = new IdempotencyStore(_dbPath);

        var first = store.TryMarkProcessed("msg-1", "worker-1");
        var second = store.TryMarkProcessed("msg-1", "worker-1");

        Assert.True(first);
        Assert.False(second, "A duplicate message id must not be reported as new processing work.");
    }

    [Fact]
    public void TryMarkProcessed_SameMessageIdFromDifferentInstance_StillOnlyProcessedOnce()
    {
        using var store = new IdempotencyStore(_dbPath);

        var firstInstance = store.TryMarkProcessed("msg-1", "worker-1");
        var secondInstance = store.TryMarkProcessed("msg-1", "worker-2");

        Assert.True(firstInstance);
        Assert.False(secondInstance, "Dedupe must hold across consumer instances, not just within one.");
        Assert.Equal("worker-1", store.GetProcessingInstance("msg-1"));
    }

    [Fact]
    public void TryMarkProcessed_DifferentMessageIds_BothReturnTrue()
    {
        using var store = new IdempotencyStore(_dbPath);

        Assert.True(store.TryMarkProcessed("msg-1", "worker-1"));
        Assert.True(store.TryMarkProcessed("msg-2", "worker-1"));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
