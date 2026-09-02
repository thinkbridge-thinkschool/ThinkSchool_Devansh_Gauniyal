namespace OutboxDemo.Domain;

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
