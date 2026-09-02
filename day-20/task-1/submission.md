# Day 20, Task 1 — The Outbox Pattern

## Notes for mentor

### The outbox table

```csharp
public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredOn { get; set; }

    public DateTime? ProcessedOn { get; set; }
    public int AttemptCount { get; set; }
    public string? Error { get; set; }

    // Claim column with owner + lease: lets a relay instance reserve a row
    // for exclusive processing without holding an open DB transaction.
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedUntil { get; set; }
}
```

Generated SQLite schema (from the EF Core migration):

```sql
CREATE TABLE "OutboxMessages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OutboxMessages" PRIMARY KEY,
    "OrderId" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "Payload" TEXT NOT NULL,
    "OccurredOn" TEXT NOT NULL,
    "ProcessedOn" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "Error" TEXT NULL,
    "ClaimedBy" TEXT NULL,
    "ClaimedUntil" TEXT NULL,
    CONSTRAINT "FK_OutboxMessages_Orders_OrderId" FOREIGN KEY ("OrderId")
        REFERENCES "Orders" ("Id") ON DELETE CASCADE
);
```

### The relay

```csharp
public class OutboxRelayService
{
    private readonly AppDbContext _db;
    private readonly IMessagePublisher _publisher;
    private readonly string _ownerId;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeProvider _clock;

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
```

### The crash scenario tested, with real evidence

Both crash windows were reproduced with a real injected failure at a precise
point (not a comment claiming what would happen), each captured as JSON by
the test that produced it — full files at
[`output/crash-a-evidence.json`](output/crash-a-evidence.json) and
[`output/crash-b-evidence.json`](output/crash-b-evidence.json).

**Crash A — dies after the DB commit, before publish is attempted.** Order +
outbox row are committed, then the relay is simply never called in that
"process lifetime." Before restart: `ProcessedOn: null, AttemptCount: 0`.
A fresh relay instance (the restart) then runs and:

```json
"after": {
  "ProcessedOn": "2026-09-02T05:16:56.751523Z",
  "AttemptCount": 0,
  "publisherDeliveryCount": 1,
  "consumerSideEffectCount": 1
}
```

**Crash B — dies after a successful publish, before `ProcessedOn` is
written.** `OutboxRelayService.CrashAfterPublishBeforeMarkSent` fires right
after publish succeeds, and the resulting `SimulatedCrashException`
propagates out uncaught. Before restart:

```json
"beforeRestart": {
  "ProcessedOn": null,
  "ClaimedBy": "relay-before-crash",
  "publisherDeliveryCountSoFar": 1,
  "consumerSideEffectCountSoFar": 1
}
```

A `ManualTimeProvider` is advanced past the dead instance's claim lease
(deterministic — no `Thread.Sleep`), and a second relay instance republishes
the same row:

```json
"afterRestart": {
  "ProcessedOn": "2026-09-02T05:17:27.774435Z",
  "publisherTotalDeliveryCount": 2,
  "consumerTotalSideEffectCount": 1
}
```

Two deliveries reached the publisher/consumer wiring; the durable
`ProcessedMessages` dedupe table only ever recorded one row for that
message id.

### Why no message is lost, and why a duplicate is harmless

No message is lost because the outbox row and the domain change commit in
one transaction (one `SaveChangesAsync` call) — a crash before that commit
means neither exists, and a crash after it means both exist, so there is
never a state where the domain change exists without its outbox row waiting
to be published. A duplicate delivery *can* occur (Crash B, above) because
marking a row sent happens in a step after the publish call, not atomically
with it — that gap is unavoidable without a distributed transaction across
the DB and the broker. The duplicate is made harmless by
`IdempotentConsumer`, which dedupes on the outbox message id against a
durable table (its primary key), so the second delivery is recognized and
the real side effect never runs twice.

### Scope resolutions

- "EF Core relationships" in the brief is a topic label on this exercise,
  not a separate deliverable — no extra entity or feature was built for it;
  `Order` and `OutboxMessage` have a real one-to-many relationship that the
  outbox mechanics exercise naturally.
- Publishing uses `InProcessFakePublisher`, an in-process fake behind
  `IMessagePublisher`, not a real broker — the brief names no specific
  broker, and a fake with an injectable failure point is what makes the
  crash proofs deterministic and testable without provisioning or paying
  for anything.

---

## What did you learn this session?

You can't put "publish to the broker" and "write to the database" in one
transaction, so the trick is making the two things that *can* share a
transaction (the order and the outbox row) the ones that must never diverge.

## What would break this?

A too-short claim lease combined with a non-durable consumer dedupe table
(an in-memory set instead of a real table) — then a slow publish gets
reclaimed and resent, and a restarted consumer forgets it already saw it.
