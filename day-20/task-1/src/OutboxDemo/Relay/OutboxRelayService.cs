using Microsoft.EntityFrameworkCore;
using OutboxDemo.Data;
using OutboxDemo.Domain;
using OutboxDemo.Publishing;

namespace OutboxDemo.Relay;

/// <summary>
/// Core relay logic, callable directly (from an endpoint, a test, or a
/// BackgroundService loop) so it never needs a timer to be exercised.
/// </summary>
public class OutboxRelayService
{
    private readonly AppDbContext _db;
    private readonly IMessagePublisher _publisher;
    private readonly string _ownerId;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Test-only hook: when set and it returns true for a message, the relay
    /// throws right after a successful publish but before the row is marked
    /// sent — the exact window CRASH B proves against.
    /// </summary>
    public Func<OutboxMessage, bool>? CrashAfterPublishBeforeMarkSent { get; set; }

    public OutboxRelayService(
        AppDbContext db,
        IMessagePublisher publisher,
        string? ownerId = null,
        TimeSpan? leaseDuration = null,
        TimeProvider? clock = null)
    {
        _db = db;
        _publisher = publisher;
        _ownerId = ownerId ?? Guid.NewGuid().ToString("N");
        _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<RelayRunResult> ProcessOnceAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var candidateIds = await _db.OutboxMessages
            .Where(m => m.ProcessedOn == null && (m.ClaimedBy == null || m.ClaimedUntil < now))
            .OrderBy(m => m.OccurredOn)
            .ThenBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var published = new List<Guid>();
        var failed = new List<Guid>();
        var skipped = new List<Guid>();

        foreach (var id in candidateIds)
        {
            var claimNow = _clock.GetUtcNow().UtcDateTime;
            var claimedRows = await _db.OutboxMessages
                .Where(m => m.Id == id && m.ProcessedOn == null && (m.ClaimedBy == null || m.ClaimedUntil < claimNow))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.ClaimedBy, _ownerId)
                    .SetProperty(m => m.ClaimedUntil, claimNow.Add(_leaseDuration)), ct);

            if (claimedRows == 0)
            {
                skipped.Add(id);
                continue;
            }

            var message = await _db.OutboxMessages.SingleAsync(m => m.Id == id, ct);

            try
            {
                await _publisher.PublishAsync(new OutboundMessage(message.Id, message.Type, message.Payload), ct);

                if (CrashAfterPublishBeforeMarkSent?.Invoke(message) == true)
                {
                    throw new SimulatedCrashException(
                        $"Simulated crash: published {message.Id} but died before marking it sent.");
                }

                message.ProcessedOn = _clock.GetUtcNow().UtcDateTime;
                message.Error = null;
                await _db.SaveChangesAsync(ct);
                published.Add(id);
            }
            catch (SimulatedCrashException)
            {
                throw;
            }
            catch (Exception ex)
            {
                message.AttemptCount += 1;
                message.Error = ex.Message;
                message.ClaimedBy = null;
                message.ClaimedUntil = null;
                await _db.SaveChangesAsync(ct);
                failed.Add(id);
            }
        }

        return new RelayRunResult(published, failed, skipped);
    }
}
