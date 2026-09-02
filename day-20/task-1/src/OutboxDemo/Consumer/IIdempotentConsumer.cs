namespace OutboxDemo.Consumer;

public interface IIdempotentConsumer
{
    Task<ConsumeResult> ConsumeAsync(Guid outboxMessageId, string payload, CancellationToken ct = default);
}
