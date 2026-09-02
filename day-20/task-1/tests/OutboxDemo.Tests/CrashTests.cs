using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutboxDemo.Consumer;
using OutboxDemo.Domain;
using OutboxDemo.Publishing;
using OutboxDemo.Relay;
using OutboxDemo.Tests.TestSupport;

namespace OutboxDemo.Tests;

/// <summary>
/// The heart of the exercise: two real, injected crash windows around the
/// relay's publish step, each captured to day-20/task-1/output as evidence.
/// </summary>
public class CrashTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task CrashA_DiesAfterCommitBeforePublish_MessageIsNotLost()
    {
        var orderId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        // The transaction that represents the domain write. Immediately
        // after this commits, the process is taken to die -- nothing below
        // runs in the same "process lifetime" as this block.
        using (var db = _database.CreateContext())
        {
            db.Orders.Add(new Order
            {
                Id = orderId,
                CustomerName = "Grace Hopper",
                Amount = 77m,
                CreatedOn = DateTime.UtcNow
            });
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = outboxId,
                OrderId = orderId,
                Type = "OrderCreated",
                Payload = $"{{\"orderId\":\"{orderId}\"}}",
                OccurredOn = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        // ---- simulated crash: process exits here, before any publish attempt ----

        object beforeState;
        using (var check = _database.CreateContext())
        {
            var row = await check.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            beforeState = new
            {
                row.Id,
                row.ProcessedOn,
                row.AttemptCount,
                consumerReceivedCount = 0
            };
            Assert.Null(row.ProcessedOn);
            Assert.Equal(0, row.AttemptCount);
        }

        // ---- process restarts: a brand-new context, publisher and relay ----
        using var restarted = _database.CreateContext();
        var consumer = new IdempotentConsumer(restarted);
        var publisher = new InProcessFakePublisher(consumer);
        var relay = new OutboxRelayService(restarted, publisher);

        var result = await relay.ProcessOnceAsync();

        Assert.Contains(outboxId, result.Published);
        Assert.Single(publisher.Deliveries);
        Assert.Single(consumer.SideEffects);

        var afterRow = await restarted.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.NotNull(afterRow.ProcessedOn);

        var evidence = new
        {
            scenario = "CRASH_A_die_after_commit_before_publish",
            claim = "No message is lost: the outbox row survives the crash and is published on the next relay run.",
            before = beforeState,
            after = new
            {
                afterRow.Id,
                afterRow.ProcessedOn,
                afterRow.AttemptCount,
                publisherDeliveryCount = publisher.Deliveries.Count,
                consumerSideEffectCount = consumer.SideEffects.Count
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(EvidencePaths.OutputDir, "crash-a-evidence.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task CrashB_DiesAfterPublishBeforeMarkSent_MessageIsDuplicatedButConsumerDedupes()
    {
        var orderId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

        using (var seed = _database.CreateContext())
        {
            seed.Orders.Add(new Order
            {
                Id = orderId,
                CustomerName = "Katherine Johnson",
                Amount = 88m,
                CreatedOn = clock.GetUtcNow().UtcDateTime
            });
            seed.OutboxMessages.Add(new OutboxMessage
            {
                Id = outboxId,
                OrderId = orderId,
                Type = "OrderCreated",
                Payload = $"{{\"orderId\":\"{orderId}\"}}",
                OccurredOn = clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }

        // The consumer's dedupe table and the fake "wire" are shared across
        // both relay attempts below -- exactly as a real, separate consumer
        // process would be shared across two deliveries from a real broker.
        using var sharedConsumerDb = _database.CreateContext();
        var consumer = new IdempotentConsumer(sharedConsumerDb);
        var publisher = new InProcessFakePublisher(consumer);

        // ---- first relay attempt: publish succeeds, then it "crashes" ----
        using (var firstAttemptDb = _database.CreateContext())
        {
            var relay = new OutboxRelayService(firstAttemptDb, publisher, ownerId: "relay-before-crash", clock: clock)
            {
                CrashAfterPublishBeforeMarkSent = _ => true
            };

            await Assert.ThrowsAsync<SimulatedCrashException>(() => relay.ProcessOnceAsync());
        }

        object beforeRestartState;
        using (var check = _database.CreateContext())
        {
            var row = await check.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            beforeRestartState = new
            {
                row.Id,
                row.ProcessedOn,
                row.ClaimedBy,
                publisherDeliveryCountSoFar = publisher.Deliveries.Count,
                consumerSideEffectCountSoFar = consumer.SideEffects.Count
            };
            // The publish genuinely happened -- the consumer already has it --
            // but the row was never marked sent, because the crash landed
            // before that SaveChangesAsync.
            Assert.Null(row.ProcessedOn);
            Assert.Equal("relay-before-crash", row.ClaimedBy);
            Assert.Single(publisher.Deliveries);
            Assert.Single(consumer.SideEffects);
        }

        // Lease held by the dead relay instance expires with real time --
        // advanced deterministically here rather than slept through.
        clock.Advance(TimeSpan.FromSeconds(31));

        // ---- process restarts: a fresh relay picks the row back up ----
        using var restartedDb = _database.CreateContext();
        var restartedRelay = new OutboxRelayService(restartedDb, publisher, ownerId: "relay-after-restart", clock: clock);
        var result = await restartedRelay.ProcessOnceAsync();

        Assert.Contains(outboxId, result.Published);

        var afterRow = await restartedDb.OutboxMessages.SingleAsync(m => m.Id == outboxId);
        Assert.NotNull(afterRow.ProcessedOn);

        var evidence = new
        {
            scenario = "CRASH_B_die_after_publish_before_mark_sent",
            claim = "A duplicate delivery occurs (publisher/consumer receive the message twice) but it is not a loss, "
                  + "and the idempotent consumer's dedupe table means only one side effect is ever applied.",
            beforeRestart = beforeRestartState,
            afterRestart = new
            {
                afterRow.Id,
                afterRow.ProcessedOn,
                publisherTotalDeliveryCount = publisher.Deliveries.Count,
                consumerTotalSideEffectCount = consumer.SideEffects.Count
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(EvidencePaths.OutputDir, "crash-b-evidence.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

        // The heart of the proof: two deliveries reached the wire/consumer,
        // but exactly one side effect was ever applied.
        Assert.Equal(2, publisher.Deliveries.Count);
        Assert.Single(consumer.SideEffects);

        var processedRows = await restartedDb.ProcessedMessages
            .Where(p => p.OutboxMessageId == outboxId)
            .ToListAsync();
        Assert.Single(processedRows);
    }

    public void Dispose() => _database.Dispose();
}
