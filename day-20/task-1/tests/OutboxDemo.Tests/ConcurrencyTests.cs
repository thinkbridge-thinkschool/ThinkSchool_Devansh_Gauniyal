using Microsoft.EntityFrameworkCore;
using OutboxDemo.Consumer;
using OutboxDemo.Domain;
using OutboxDemo.Publishing;
using OutboxDemo.Relay;
using OutboxDemo.Tests.TestSupport;

namespace OutboxDemo.Tests;

public class ConcurrencyTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task TwoConcurrentRelays_DoNotDoublePublish_TheSameRow()
    {
        var orderId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        using (var seed = _database.CreateContext())
        {
            seed.Orders.Add(new Order
            {
                Id = orderId,
                CustomerName = "Concurrent Customer",
                Amount = 5m,
                CreatedOn = DateTime.UtcNow
            });
            seed.OutboxMessages.Add(new OutboxMessage
            {
                Id = outboxId,
                OrderId = orderId,
                Type = "OrderCreated",
                Payload = "{}",
                OccurredOn = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        using var consumerDb = _database.CreateContext();
        var consumer = new IdempotentConsumer(consumerDb);
        var publisher = new InProcessFakePublisher(consumer);

        using var db1 = _database.CreateContext();
        using var db2 = _database.CreateContext();
        var relay1 = new OutboxRelayService(db1, publisher, ownerId: "relay-1");
        var relay2 = new OutboxRelayService(db2, publisher, ownerId: "relay-2");

        var results = await Task.WhenAll(relay1.ProcessOnceAsync(), relay2.ProcessOnceAsync());

        // Exactly one of the two relay instances actually published the row.
        // The other either lost the claim race (saw it, got 0 rows affected)
        // or never saw it as a candidate at all (it was already claimed by
        // the time its own SELECT ran) -- both are safe outcomes, so only
        // the absence of a double publish is asserted here.
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(1, results.Sum(r => r.Published.Count));

        var row = await consumerDb.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.NotNull(row.ProcessedOn);
    }

    public void Dispose() => _database.Dispose();
}
