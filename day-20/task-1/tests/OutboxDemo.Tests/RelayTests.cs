using Microsoft.EntityFrameworkCore;
using OutboxDemo.Consumer;
using OutboxDemo.Domain;
using OutboxDemo.Publishing;
using OutboxDemo.Relay;
using OutboxDemo.Tests.TestSupport;

namespace OutboxDemo.Tests;

public class RelayTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private async Task<Guid> SeedOrderWithOutboxMessageAsync(DateTime occurredOn)
    {
        var orderId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        using var db = _database.CreateContext();
        db.Orders.Add(new Order
        {
            Id = orderId,
            CustomerName = "Ada Lovelace",
            Amount = 10m,
            CreatedOn = occurredOn
        });
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            OrderId = orderId,
            Type = "OrderCreated",
            Payload = $"{{\"orderId\":\"{orderId}\"}}",
            OccurredOn = occurredOn
        });
        await db.SaveChangesAsync();
        return outboxId;
    }

    [Fact]
    public async Task Relay_PublishesUnprocessedRow_AndMarksItSent()
    {
        var outboxId = await SeedOrderWithOutboxMessageAsync(DateTime.UtcNow);

        using var db = _database.CreateContext();
        var consumer = new IdempotentConsumer(db);
        var publisher = new InProcessFakePublisher(consumer);
        var relay = new OutboxRelayService(db, publisher);

        var result = await relay.ProcessOnceAsync();

        Assert.Contains(outboxId, result.Published);
        Assert.Single(publisher.Deliveries);
        Assert.Equal(outboxId, publisher.Deliveries.Single().OutboxMessageId);

        var row = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.NotNull(row.ProcessedOn);
        Assert.Equal(0, row.AttemptCount);
        Assert.Null(row.Error);
    }

    [Fact]
    public async Task PublishFailure_LeavesRowUnprocessed_IncrementsAttempts_RecordsError()
    {
        var outboxId = await SeedOrderWithOutboxMessageAsync(DateTime.UtcNow);

        using var db = _database.CreateContext();
        var consumer = new IdempotentConsumer(db);
        var publisher = new InProcessFakePublisher(consumer)
        {
            FailureInjector = (_, _) => new InvalidOperationException("downstream broker unreachable")
        };
        var relay = new OutboxRelayService(db, publisher);

        var result = await relay.ProcessOnceAsync();

        Assert.Contains(outboxId, result.Failed);
        Assert.Empty(publisher.Deliveries);

        var row = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.Null(row.ProcessedOn);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("downstream broker unreachable", row.Error);
        Assert.Null(row.ClaimedBy);
    }

    [Fact]
    public async Task FailedMessage_SucceedsOnRetry_OnANextRun()
    {
        var outboxId = await SeedOrderWithOutboxMessageAsync(DateTime.UtcNow);

        using var db = _database.CreateContext();
        var consumer = new IdempotentConsumer(db);
        var shouldFail = true;
        var publisher = new InProcessFakePublisher(consumer)
        {
            FailureInjector = (_, _) => shouldFail ? new InvalidOperationException("transient") : null
        };
        var relay = new OutboxRelayService(db, publisher);

        await relay.ProcessOnceAsync();
        shouldFail = false;
        var secondRun = await relay.ProcessOnceAsync();

        Assert.Contains(outboxId, secondRun.Published);
        var row = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.NotNull(row.ProcessedOn);
        Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task Relay_ProcessesRows_InOccurredOnOrder()
    {
        var baseTime = DateTime.UtcNow;
        // Seeded out of OccurredOn order on purpose.
        var second = await SeedOrderWithOutboxMessageAsync(baseTime.AddSeconds(2));
        var first = await SeedOrderWithOutboxMessageAsync(baseTime.AddSeconds(1));
        var third = await SeedOrderWithOutboxMessageAsync(baseTime.AddSeconds(3));

        using var db = _database.CreateContext();
        var consumer = new IdempotentConsumer(db);
        var publisher = new InProcessFakePublisher(consumer);
        var relay = new OutboxRelayService(db, publisher);

        await relay.ProcessOnceAsync();

        var deliveredOrder = publisher.Deliveries.Select(d => d.OutboxMessageId).ToArray();
        Assert.Equal(new[] { first, second, third }, deliveredOrder);
    }

    public void Dispose() => _database.Dispose();
}
