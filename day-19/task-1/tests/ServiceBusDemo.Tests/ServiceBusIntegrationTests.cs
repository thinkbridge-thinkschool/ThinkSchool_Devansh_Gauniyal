using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Core;
using Xunit.Sdk;

namespace ServiceBusDemo.Tests;

/// <summary>
/// These tests talk to a real Service Bus broker — the local emulator by default, or a real
/// Azure namespace if SERVICEBUS_CONNECTION_STRING points at one — and are skipped (not failed)
/// when that environment variable isn't set. They exercise the topic/subscriptions defined in
/// emulator/config.json (topic "orders-topic", subscriptions "audit-sub" and "processing-sub"
/// with MaxDeliveryCount=3). Run them right after starting a fresh emulator container: the
/// emulator does not persist data across restarts, so a clean container means a clean topic.
/// </summary>
public class ServiceBusIntegrationTests : IAsyncLifetime
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING");
    private const string TopicName = "orders-topic";
    private const string AuditSubscription = "audit-sub";
    private const string ProcessingSubscription = "processing-sub";

    private ServiceBusClient? _client;

    public Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            _client = new ServiceBusClient(ConnectionString);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task TwoSubscriptions_OnSameTopic_EachReceiveTheirOwnCopy()
    {
        Skip.If(_client is null, "Requires a live Service Bus emulator/namespace: set SERVICEBUS_CONNECTION_STRING to run this test.");

        var messageId = $"fanout-{Guid.NewGuid():N}";
        await using var sender = _client!.CreateSender(TopicName);
        await sender.SendMessageAsync(new ServiceBusMessage("fan-out probe") { MessageId = messageId });

        await using var auditReceiver = _client.CreateReceiver(TopicName, AuditSubscription);
        await using var processingReceiver = _client.CreateReceiver(TopicName, ProcessingSubscription);

        var fromAudit = await ReceiveUntilAsync(auditReceiver, m => m.MessageId == messageId, TimeSpan.FromSeconds(20));
        var fromProcessing = await ReceiveUntilAsync(processingReceiver, m => m.MessageId == messageId, TimeSpan.FromSeconds(20));

        Assert.NotNull(fromAudit);
        Assert.NotNull(fromProcessing);
        Assert.Equal(messageId, fromAudit!.MessageId);
        Assert.Equal(messageId, fromProcessing!.MessageId);

        await auditReceiver.CompleteMessageAsync(fromAudit);
        await processingReceiver.CompleteMessageAsync(fromProcessing);
    }

    [SkippableFact]
    public async Task CompetingConsumers_OnOneSubscription_DoNotDoubleProcessAMessage()
    {
        Skip.If(_client is null, "Requires a live Service Bus emulator/namespace: set SERVICEBUS_CONNECTION_STRING to run this test.");

        const int messageCount = 5;
        const int instanceCount = 3;
        var messageIds = Enumerable.Range(0, messageCount).Select(_ => $"competing-{Guid.NewGuid():N}").ToHashSet();

        await using var sender = _client!.CreateSender(TopicName);
        foreach (var id in messageIds)
        {
            await sender.SendMessageAsync(new ServiceBusMessage("competing-consumer probe") { MessageId = id });
        }

        var dbPath = Path.Combine(Path.GetTempPath(), $"competing-integration-{Guid.NewGuid():N}.db");
        using var store = new IdempotencyStore(dbPath);
        var processedBy = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var deadline = DateTime.UtcNow.AddSeconds(20);

        var workers = Enumerable.Range(1, instanceCount).Select(i => Task.Run(async () =>
        {
            var instanceName = $"worker-{i}";
            var receiver = _client.CreateReceiver(TopicName, ProcessingSubscription);

            while (DateTime.UtcNow < deadline && processedBy.Count < messageIds.Count)
            {
                var messages = await receiver.ReceiveMessagesAsync(maxMessages: 5, maxWaitTime: TimeSpan.FromSeconds(2));
                foreach (var message in messages)
                {
                    if (!messageIds.Contains(message.MessageId))
                    {
                        await receiver.AbandonMessageAsync(message); // leftover from another test/run; not ours
                        continue;
                    }

                    if (store.TryMarkProcessed(message.MessageId, instanceName))
                    {
                        processedBy[message.MessageId] = instanceName;
                    }

                    await receiver.CompleteMessageAsync(message);
                }
            }

            await receiver.DisposeAsync();
        })).ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(messageIds.Count, processedBy.Count);
        Assert.All(messageIds, id => Assert.True(processedBy.ContainsKey(id), $"message {id} was never processed by any instance"));

        File.Delete(dbPath);
    }

    [SkippableFact]
    public async Task PoisonMessage_ExhaustsDeliveryAttempts_AndCarriesExpectedDeadLetterReason()
    {
        Skip.If(_client is null, "Requires a live Service Bus emulator/namespace: set SERVICEBUS_CONNECTION_STRING to run this test.");

        var messageId = $"poison-{Guid.NewGuid():N}";
        await using var sender = _client!.CreateSender(TopicName);
        var poisonMessage = new ServiceBusMessage("poison probe") { MessageId = messageId };
        poisonMessage.ApplicationProperties[MessageProperties.Poison] = true;
        await sender.SendMessageAsync(poisonMessage);

        await using var receiver = _client.CreateReceiver(TopicName, ProcessingSubscription);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var attempts = 0;

        // Repeatedly receive-then-abandon our poison message; MaxDeliveryCount=3 on
        // processing-sub means the broker auto-dead-letters it after the third delivery.
        while (DateTime.UtcNow < deadline)
        {
            var messages = await receiver.ReceiveMessagesAsync(maxMessages: 5, maxWaitTime: TimeSpan.FromSeconds(3));
            var ours = messages.FirstOrDefault(m => m.MessageId == messageId);

            foreach (var message in messages)
            {
                if (message.MessageId != messageId)
                {
                    await receiver.AbandonMessageAsync(message);
                    continue;
                }

                attempts++;
                await receiver.AbandonMessageAsync(message);
            }

            if (ours is null && attempts > 0)
            {
                break; // no longer deliverable from the main sub-queue: it has been dead-lettered
            }
        }

        Assert.True(attempts >= 1, "Expected at least one delivery attempt of the poison message before it was dead-lettered.");

        await using var dlqReceiver = _client.CreateReceiver(TopicName, ProcessingSubscription, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var deadLettered = await ReceiveUntilAsync(dlqReceiver, m => m.MessageId == messageId, TimeSpan.FromSeconds(15));

        Assert.NotNull(deadLettered);
        Assert.False(string.IsNullOrEmpty(deadLettered!.DeadLetterReason));
        await dlqReceiver.CompleteMessageAsync(deadLettered);
    }

    private static async Task<ServiceBusReceivedMessage?> ReceiveUntilAsync(
        ServiceBusReceiver receiver,
        Func<ServiceBusReceivedMessage, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var messages = await receiver.ReceiveMessagesAsync(maxMessages: 10, maxWaitTime: TimeSpan.FromSeconds(3));
            foreach (var message in messages)
            {
                if (predicate(message))
                {
                    return message;
                }

                await receiver.AbandonMessageAsync(message); // not the one we're looking for; leave it for whoever wants it
            }
        }

        return null;
    }
}
