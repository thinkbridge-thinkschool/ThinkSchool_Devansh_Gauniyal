namespace ServiceBusDemo.Core;

/// <summary>
/// Mirrors the delivery-count bookkeeping that Service Bus itself performs on a message
/// (queue/subscription MaxDeliveryCount). This class is a local, unit-testable stand-in
/// for that broker behavior — it does NOT talk to Service Bus. The consumer also reads the
/// real, broker-assigned <c>ServiceBusReceivedMessage.DeliveryCount</c> at runtime; that
/// broker-reported count is the authoritative one and is what actually triggers dead-lettering
/// in the emulator/Azure. This tracker exists so the "N failures exhaust delivery attempts"
/// logic can be proven deterministically in a unit test, without a live namespace.
/// </summary>
public sealed class DeliveryTracker
{
    private readonly Dictionary<string, int> _attempts = new();

    public int MaxDeliveryCount { get; }

    public DeliveryTracker(int maxDeliveryCount)
    {
        if (maxDeliveryCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDeliveryCount), "Max delivery count must be at least 1.");
        }

        MaxDeliveryCount = maxDeliveryCount;
    }

    /// <summary>Records one delivery attempt for the message and returns the new attempt count.</summary>
    public int RecordAttempt(string messageId)
    {
        _attempts.TryGetValue(messageId, out var count);
        count++;
        _attempts[messageId] = count;
        return count;
    }

    public int GetAttemptCount(string messageId) =>
        _attempts.TryGetValue(messageId, out var count) ? count : 0;

    /// <summary>True once a message's recorded attempts have reached MaxDeliveryCount.</summary>
    public bool ShouldDeadLetter(string messageId) =>
        GetAttemptCount(messageId) >= MaxDeliveryCount;
}
