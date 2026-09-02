using Microsoft.EntityFrameworkCore;
using OutboxDemo.Consumer;
using OutboxDemo.Tests.TestSupport;

namespace OutboxDemo.Tests;

public class IdempotentConsumerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task DuplicateDelivery_OfTheSameMessageId_IsProcessedExactlyOnce()
    {
        var messageId = Guid.NewGuid();
        using var db = _database.CreateContext();
        var consumer = new IdempotentConsumer(db);

        var first = await consumer.ConsumeAsync(messageId, "{\"payload\":1}");
        var second = await consumer.ConsumeAsync(messageId, "{\"payload\":1}");

        Assert.Equal(ConsumeResult.Processed, first);
        Assert.Equal(ConsumeResult.Duplicate, second);
        Assert.Single(consumer.SideEffects);

        var persisted = await db.ProcessedMessages.Where(p => p.OutboxMessageId == messageId).ToListAsync();
        Assert.Single(persisted);
    }

    public void Dispose() => _database.Dispose();
}
