using Microsoft.EntityFrameworkCore;
using OutboxDemo.Domain;
using OutboxDemo.Tests.TestSupport;

namespace OutboxDemo.Tests;

public class AtomicityTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task DomainChangeAndOutboxRow_CommitTogether_InOneSaveChanges()
    {
        var orderId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        using (var db = _database.CreateContext())
        {
            db.Orders.Add(new Order
            {
                Id = orderId,
                CustomerName = "Ada Lovelace",
                Amount = 42.50m,
                CreatedOn = DateTime.UtcNow
            });
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = outboxId,
                OrderId = orderId,
                Type = "OrderCreated",
                Payload = "{}",
                OccurredOn = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using (var verify = _database.CreateContext())
        {
            Assert.True(await verify.Orders.AnyAsync(o => o.Id == orderId));
            Assert.True(await verify.OutboxMessages.AnyAsync(m => m.Id == outboxId));
        }
    }

    [Fact]
    public async Task WhenOutboxInsertFails_DomainChangeDoesNotPersist()
    {
        var conflictingOutboxId = Guid.NewGuid();

        using (var seed = _database.CreateContext())
        {
            var seedOrderId = Guid.NewGuid();
            seed.Orders.Add(new Order
            {
                Id = seedOrderId,
                CustomerName = "Existing Customer",
                Amount = 1m,
                CreatedOn = DateTime.UtcNow
            });
            seed.OutboxMessages.Add(new OutboxMessage
            {
                Id = conflictingOutboxId,
                OrderId = seedOrderId,
                Type = "OrderCreated",
                Payload = "{}",
                OccurredOn = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var newOrderId = Guid.NewGuid();

        using (var db = _database.CreateContext())
        {
            db.Orders.Add(new Order
            {
                Id = newOrderId,
                CustomerName = "Should Not Persist",
                Amount = 99m,
                CreatedOn = DateTime.UtcNow
            });
            // Reuses an existing primary key: the outbox insert must fail,
            // and it must take the order insert down with it.
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = conflictingOutboxId,
                OrderId = newOrderId,
                Type = "OrderCreated",
                Payload = "{}",
                OccurredOn = DateTime.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        using (var verify = _database.CreateContext())
        {
            Assert.False(await verify.Orders.AnyAsync(o => o.Id == newOrderId));
        }
    }

    public void Dispose() => _database.Dispose();
}
