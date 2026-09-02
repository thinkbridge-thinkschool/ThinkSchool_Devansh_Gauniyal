using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OutboxDemo.Data;
using OutboxDemo.Domain;

namespace OutboxDemo.Consumer;

/// <summary>
/// Dedupes on the outbox message id via a durable table with that id as its
/// primary key, so a second delivery is rejected even if this consumer's own
/// process restarted between the two deliveries.
/// </summary>
public class IdempotentConsumer : IIdempotentConsumer
{
    private readonly AppDbContext _db;
    private readonly ConcurrentQueue<(Guid MessageId, string Payload)> _sideEffects = new();

    public IdempotentConsumer(AppDbContext db)
    {
        _db = db;
    }

    public IReadOnlyCollection<(Guid MessageId, string Payload)> SideEffects => _sideEffects.ToArray();

    public async Task<ConsumeResult> ConsumeAsync(Guid outboxMessageId, string payload, CancellationToken ct = default)
    {
        var alreadyProcessed = await _db.ProcessedMessages
            .AnyAsync(p => p.OutboxMessageId == outboxMessageId, ct);
        if (alreadyProcessed)
        {
            return ConsumeResult.Duplicate;
        }

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            OutboxMessageId = outboxMessageId,
            ProcessedOn = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another delivery of the same message: the
            // primary key on OutboxMessageId rejected the second insert.
            _db.Entry(_db.ChangeTracker.Entries<ProcessedMessage>().First().Entity).State = EntityState.Detached;
            return ConsumeResult.Duplicate;
        }

        _sideEffects.Enqueue((outboxMessageId, payload));
        return ConsumeResult.Processed;
    }
}
