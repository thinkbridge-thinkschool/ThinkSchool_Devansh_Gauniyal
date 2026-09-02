using System.Collections.Concurrent;

namespace OutboxDemo.Publishing;

/// <summary>
/// Stands in for a real broker (Service Bus, SQS, Kafka...) so the outbox
/// mechanics can be tested deterministically without provisioning anything.
/// Delivers straight into the consumer, and can be told to fail or to throw
/// a SimulatedCrashException on a specific call for the crash proofs.
/// </summary>
public class InProcessFakePublisher : IMessagePublisher
{
    private readonly Consumer.IIdempotentConsumer _consumer;
    private readonly ConcurrentQueue<OutboundMessage> _deliveries = new();
    private int _callCount;

    public Func<OutboundMessage, int, Exception?>? FailureInjector { get; set; }

    public InProcessFakePublisher(Consumer.IIdempotentConsumer consumer)
    {
        _consumer = consumer;
    }

    public IReadOnlyCollection<OutboundMessage> Deliveries => _deliveries.ToArray();
    public int CallCount => _callCount;

    public async Task PublishAsync(OutboundMessage message, CancellationToken ct = default)
    {
        var callNumber = Interlocked.Increment(ref _callCount);

        var injected = FailureInjector?.Invoke(message, callNumber);
        if (injected is not null)
        {
            throw injected;
        }

        _deliveries.Enqueue(message);
        await _consumer.ConsumeAsync(message.OutboxMessageId, message.Payload, ct);
    }
}
