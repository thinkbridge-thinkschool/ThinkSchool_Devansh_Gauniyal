namespace Capstone.Invoicing.Infrastructure.Persistence;

// The transactional outbox row for DESIGN.md's InvoiceApproved integration event -
// a plain persistence-layer record, not a domain concept the Invoice aggregate
// itself needs to know about. Payload is pre-serialized JSON (not a typed column)
// so this table never needs to change shape as integration-event payloads evolve;
// EventType exists so a future second integration event doesn't need a second
// table. PublishedAt is set by OutboxRelayBackgroundService (host/Capstone.Web)
// once the row has actually been handed to Service Bus - null means "still
// pending", which is exactly what the relay polls for.
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}
