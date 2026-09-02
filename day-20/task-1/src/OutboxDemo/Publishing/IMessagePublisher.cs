namespace OutboxDemo.Publishing;

public record OutboundMessage(Guid OutboxMessageId, string Type, string Payload);

public interface IMessagePublisher
{
    Task PublishAsync(OutboundMessage message, CancellationToken ct = default);
}
