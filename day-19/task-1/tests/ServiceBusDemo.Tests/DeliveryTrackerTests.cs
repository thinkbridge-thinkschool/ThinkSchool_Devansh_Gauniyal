using ServiceBusDemo.Core;

namespace ServiceBusDemo.Tests;

public class DeliveryTrackerTests
{
    [Fact]
    public void ShouldDeadLetter_BeforeMaxDeliveryCount_IsFalse()
    {
        var tracker = new DeliveryTracker(maxDeliveryCount: 3);

        tracker.RecordAttempt("poison-1");
        tracker.RecordAttempt("poison-1");

        Assert.False(tracker.ShouldDeadLetter("poison-1"), "Two attempts against a max of three must not be dead-letter eligible yet.");
    }

    [Fact]
    public void ShouldDeadLetter_AtMaxDeliveryCount_IsTrue()
    {
        var tracker = new DeliveryTracker(maxDeliveryCount: 3);

        tracker.RecordAttempt("poison-1");
        tracker.RecordAttempt("poison-1");
        tracker.RecordAttempt("poison-1");

        Assert.True(tracker.ShouldDeadLetter("poison-1"), "A handler that throws on every attempt must exhaust delivery attempts at exactly MaxDeliveryCount.");
    }

    [Fact]
    public void ShouldDeadLetter_UnknownMessageId_IsFalse()
    {
        var tracker = new DeliveryTracker(maxDeliveryCount: 3);

        Assert.False(tracker.ShouldDeadLetter("never-seen"));
    }

    [Fact]
    public void RecordAttempt_TracksEachMessageIdIndependently()
    {
        var tracker = new DeliveryTracker(maxDeliveryCount: 2);

        tracker.RecordAttempt("a");
        tracker.RecordAttempt("b");
        tracker.RecordAttempt("b");

        Assert.False(tracker.ShouldDeadLetter("a"));
        Assert.True(tracker.ShouldDeadLetter("b"));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMaxDeliveryCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeliveryTracker(0));
    }
}
