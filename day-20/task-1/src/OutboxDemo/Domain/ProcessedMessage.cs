namespace OutboxDemo.Domain;

/// <summary>
/// Durable dedupe record kept by the consumer side. The primary key is the
/// outbox message id, so a second delivery of the same message can never
/// produce a second row here.
/// </summary>
public class ProcessedMessage
{
    public Guid OutboxMessageId { get; set; }
    public DateTime ProcessedOn { get; set; }
}
