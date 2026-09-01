using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Core;

var connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "SERVICEBUS_CONNECTION_STRING is not set. Point it at the local Service Bus emulator " +
        "(or a real namespace) before running the consumer. Never hardcode this value in source.");

var topicName = Environment.GetEnvironmentVariable("SERVICEBUS_TOPIC") ?? "orders-topic";
var auditSubscription = Environment.GetEnvironmentVariable("SERVICEBUS_AUDIT_SUBSCRIPTION") ?? "audit-sub";
var processingSubscription = Environment.GetEnvironmentVariable("SERVICEBUS_PROCESSING_SUBSCRIPTION") ?? "processing-sub";
var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "output";
Directory.CreateDirectory(outputDir);

await using var client = new ServiceBusClient(connectionString);

var scenario = args.Length > 0 ? args[0] : "";

switch (scenario)
{
    case "fanout":
        await RunFanOutAsync();
        break;
    case "competing":
        {
            var instanceCount = args.Length > 1 ? int.Parse(args[1]) : 3;
            var maxWaitSeconds = args.Length > 2 ? int.Parse(args[2]) : 15;
            await RunCompetingConsumersAsync(instanceCount, maxWaitSeconds);
            break;
        }
    case "poison-handle":
        {
            var maxWaitSeconds = args.Length > 1 ? int.Parse(args[1]) : 30;
            await RunPoisonHandlingAsync(maxWaitSeconds);
            break;
        }
    case "dlq-read":
        await RunDlqReadAsync();
        break;
    default:
        Console.WriteLine("Usage: dotnet run -- <fanout|competing|poison-handle|dlq-read> [args]");
        break;
}

// Fan-out proof: audit-sub is never touched by the competing-consumer step, so whatever
// message ids show up here are its own independent copy of whatever was published to the topic.
async Task RunFanOutAsync()
{
    var receiver = client.CreateReceiver(topicName, auditSubscription);
    var received = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(10));

    var ids = new List<string>();
    foreach (var message in received)
    {
        ids.Add(message.MessageId);
        await receiver.CompleteMessageAsync(message);
        Console.WriteLine($"[audit-sub] received its own copy of MessageId={message.MessageId}");
    }

    await WriteEvidenceAsync("fanout-evidence.json", new
    {
        subscription = auditSubscription,
        receivedMessageIds = ids,
        count = ids.Count,
    });

    await receiver.DisposeAsync();
}

// Competing consumers: N concurrent receivers pull from the SAME subscription. Service Bus's
// message lock guarantees a given message is only ever handed to one receiver at a time; the
// IdempotencyStore is the application-level backstop that also catches redeliveries/duplicate
// message ids (e.g. the "duplicate" publisher scenario), regardless of which instance sees them.
async Task RunCompetingConsumersAsync(int instanceCount, int maxWaitSeconds)
{
    using var store = new IdempotencyStore(Path.Combine(outputDir, "idempotency.db"));
    var deliveries = new ConcurrentBag<DeliveryRecord>();

    var workers = Enumerable.Range(1, instanceCount).Select(i => Task.Run(async () =>
    {
        var instanceName = $"worker-{i}";
        var receiver = client.CreateReceiver(topicName, processingSubscription);
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var messages = await receiver.ReceiveMessagesAsync(maxMessages: 5, maxWaitTime: TimeSpan.FromSeconds(2));
            if (messages.Count == 0)
            {
                continue;
            }

            foreach (var message in messages)
            {
                if (IsPoison(message))
                {
                    // The poison message is handled by the dedicated poison-handle command.
                    await receiver.AbandonMessageAsync(message);
                    continue;
                }

                var wasNewProcessing = store.TryMarkProcessed(message.MessageId, instanceName);
                deliveries.Add(new DeliveryRecord(message.MessageId, instanceName, wasNewProcessing, message.DeliveryCount));
                await receiver.CompleteMessageAsync(message);

                Console.WriteLine(wasNewProcessing
                    ? $"[{instanceName}] processed MessageId={message.MessageId} (first time)"
                    : $"[{instanceName}] saw MessageId={message.MessageId} again — already processed by {store.GetProcessingInstance(message.MessageId)}, skipping work");
            }
        }

        await receiver.DisposeAsync();
    }));

    await Task.WhenAll(workers);

    var deliveryList = deliveries.ToList();
    var byMessageId = deliveryList.GroupBy(d => d.MessageId).ToList();
    var doubleProcessed = byMessageId.Where(g => g.Count(d => d.WasNewProcessing) > 1).ToList();

    await WriteEvidenceAsync("processing-evidence.json", new
    {
        subscription = processingSubscription,
        instanceCount,
        totalDeliveries = deliveryList.Count,
        distinctMessageIds = byMessageId.Count,
        deliveries = deliveryList,
        anyMessageProcessedMoreThanOnce = doubleProcessed.Count > 0,
    });

    Console.WriteLine($"Distinct message ids seen: {byMessageId.Count}; total deliveries (incl. duplicates/redeliveries): {deliveryList.Count}; any double-processed: {doubleProcessed.Count > 0}");
}

// Poison handling: the handler always throws for a message flagged Poison=true. We abandon
// (not complete) each time, so Service Bus redelivers it; the real, broker-assigned
// DeliveryCount is read straight off the message. Once it reaches the subscription's
// configured MaxDeliveryCount, the broker itself moves the message to the dead-letter
// sub-queue with no further action from us.
async Task RunPoisonHandlingAsync(int maxWaitSeconds)
{
    var receiver = client.CreateReceiver(topicName, processingSubscription);
    var attempts = new List<object>();
    var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);

    while (DateTime.UtcNow < deadline)
    {
        var messages = await receiver.ReceiveMessagesAsync(maxMessages: 1, maxWaitTime: TimeSpan.FromSeconds(5));
        if (messages.Count == 0)
        {
            continue;
        }

        var message = messages[0];
        if (!IsPoison(message))
        {
            await receiver.AbandonMessageAsync(message);
            continue;
        }

        Console.WriteLine($"Poison MessageId={message.MessageId} delivered, broker DeliveryCount={message.DeliveryCount} — handler will always throw.");

        try
        {
            throw new InvalidOperationException("Simulated permanent handler failure for the poison message.");
        }
        catch (Exception ex)
        {
            attempts.Add(new
            {
                messageId = message.MessageId,
                brokerDeliveryCount = message.DeliveryCount,
                attemptedAtUtc = DateTime.UtcNow,
                exception = ex.Message,
            });
            await receiver.AbandonMessageAsync(message);
        }
    }

    await WriteEvidenceAsync("poison-handling-log.json", new { subscription = processingSubscription, attempts });
    await receiver.DisposeAsync();
    Console.WriteLine($"Poison handling loop finished after {attempts.Count} attempt(s). Run 'dlq-read' next to confirm it landed in the DLQ.");
}

// Reads the dead-letter sub-queue directly. DeadLetterReason / DeadLetterErrorDescription
// are set by the broker itself (not by our code) once MaxDeliveryCount is exceeded.
async Task RunDlqReadAsync()
{
    var dlqReceiver = client.CreateReceiver(topicName, processingSubscription, new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter,
    });

    var messages = await dlqReceiver.ReceiveMessagesAsync(maxMessages: 10, maxWaitTime: TimeSpan.FromSeconds(10));
    var evidence = messages.Select(m => new
    {
        messageId = m.MessageId,
        deadLetterReason = m.DeadLetterReason,
        deadLetterErrorDescription = m.DeadLetterErrorDescription,
        brokerDeliveryCount = m.DeliveryCount,
        body = m.Body.ToString(),
    }).ToList();

    foreach (var message in messages)
    {
        Console.WriteLine($"[DLQ] MessageId={message.MessageId} reason='{message.DeadLetterReason}' description='{message.DeadLetterErrorDescription}'");
        await dlqReceiver.CompleteMessageAsync(message);
    }

    await WriteEvidenceAsync("dlq-evidence.json", new { subscription = processingSubscription, deadLetteredMessages = evidence });
    await dlqReceiver.DisposeAsync();
}

static bool IsPoison(ServiceBusReceivedMessage message) =>
    message.ApplicationProperties.TryGetValue(MessageProperties.Poison, out var value) && value is true;

async Task WriteEvidenceAsync(string fileName, object payload)
{
    var path = Path.Combine(outputDir, fileName);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote evidence: {path}");
}

internal sealed record DeliveryRecord(string MessageId, string Instance, bool WasNewProcessing, int BrokerDeliveryCount);
