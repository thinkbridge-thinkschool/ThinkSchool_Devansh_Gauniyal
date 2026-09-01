using System.Collections.Concurrent;
using ServiceBusDemo.Core;

namespace ServiceBusDemo.Tests;

/// <summary>
/// Proves the dedupe mechanism that backs the competing-consumer worker without needing a live
/// Service Bus connection: several "consumer instances" race, concurrently and deliberately
/// synchronized to start at the same instant, to process the same handful of message ids against
/// one shared IdempotencyStore — exactly the situation a real competing-consumer subscription
/// creates when several worker processes contend for the same messages. Uses a Barrier instead of
/// Thread.Sleep so the race is real and the test is still deterministic.
/// </summary>
public class CompetingConsumersSimulationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"competing-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ConcurrentInstances_RacingOnSameMessageIds_EachMessageProcessedExactlyOnce()
    {
        const int instanceCount = 8;
        const int messageCount = 5;
        var messageIds = Enumerable.Range(0, messageCount).Select(i => $"msg-{i}").ToArray();

        using var store = new IdempotencyStore(_dbPath);
        var results = new ConcurrentBag<(string MessageId, string Instance, bool WasNew)>();
        using var barrier = new Barrier(instanceCount);

        var workers = Enumerable.Range(0, instanceCount).Select(i => Task.Run(() =>
        {
            var instanceName = $"worker-{i}";
            barrier.SignalAndWait(); // line every instance up so they all hit TryMarkProcessed at once

            foreach (var messageId in messageIds)
            {
                var wasNew = store.TryMarkProcessed(messageId, instanceName);
                results.Add((messageId, instanceName, wasNew));
            }
        }));

        await Task.WhenAll(workers);

        foreach (var messageId in messageIds)
        {
            var winners = results.Count(r => r.MessageId == messageId && r.WasNew);
            Assert.Equal(1, winners); // exactly one instance actually "processed" each message id
        }

        Assert.Equal(instanceCount * messageCount, results.Count); // every instance attempted every id
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
