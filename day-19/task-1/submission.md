# Day 19 Task 1 — Submission

## Notes for mentor

**Evidence status — please read first:** the code, offline unit tests, and mutation check below are real and were actually run (output captured in this session). The **live broker evidence** (Docker-based Service Bus emulator run — fan-out proof, competing-consumer proof, and the poison-message-in-DLQ proof) could **not** be captured: pulling the emulator's required container images from `mcr.microsoft.com` stalled repeatedly on this network (confirmed via direct diagnostics — DNS and TLS both healthy, small API calls succeed instantly, but sustained blob downloads throttle to a crawl and time out; no VPN or proxy is configured locally, so this is an external network condition, not a local misconfiguration). I am not fabricating DLQ evidence to fill this gap. `README.md`'s "Running it yourself" section has the exact commands to produce the missing `output/*.json` evidence files the moment the emulator can be pulled — none of the application code changes when that happens.

### Publisher source (`src/Publisher/Program.cs`)

```csharp
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Core;

var connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "SERVICEBUS_CONNECTION_STRING is not set. Point it at the local Service Bus emulator " +
        "(or a real namespace) before running the publisher. Never hardcode this value in source.");

var topicName = Environment.GetEnvironmentVariable("SERVICEBUS_TOPIC") ?? "orders-topic";
var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "output";
Directory.CreateDirectory(outputDir);

await using var client = new ServiceBusClient(connectionString);
var sender = client.CreateSender(topicName);

var scenario = args.Length > 0 ? args[0] : "batch";

switch (scenario)
{
    case "batch":
        {
            var count = args.Length > 1 ? int.Parse(args[1]) : 5;
            var publishedIds = new List<string>();

            for (var i = 0; i < count; i++)
            {
                var order = OrderMessage.NewRandom();
                var messageId = Guid.NewGuid().ToString("N");
                var message = new ServiceBusMessage(JsonSerializer.Serialize(order))
                {
                    MessageId = messageId,
                    ContentType = "application/json",
                };

                await sender.SendMessageAsync(message);
                publishedIds.Add(messageId);
                Console.WriteLine($"Published order {order.OrderId} with MessageId={messageId}");
            }

            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "published-message-ids.json"),
                JsonSerializer.Serialize(publishedIds, new JsonSerializerOptions { WriteIndented = true }));
            break;
        }

    case "duplicate":
        {
            if (args.Length < 2)
            {
                throw new ArgumentException("Usage: dotnet run -- duplicate <existing-message-id>");
            }

            var messageId = args[1];
            var order = OrderMessage.NewRandom();
            var message = new ServiceBusMessage(JsonSerializer.Serialize(order))
            {
                MessageId = messageId,
                ContentType = "application/json",
            };

            // Simulates an at-least-once producer retry (or a manual redelivery) that reuses
            // the exact same MessageId as an earlier publish. The consumer's IdempotencyStore
            // is what makes this a no-op on the processing side.
            await sender.SendMessageAsync(message);
            Console.WriteLine($"Re-published duplicate with MessageId={messageId}");
            break;
        }

    case "poison":
        {
            var order = OrderMessage.NewRandom();
            var messageId = Guid.NewGuid().ToString("N");
            var message = new ServiceBusMessage(JsonSerializer.Serialize(order))
            {
                MessageId = messageId,
                ContentType = "application/json",
            };
            message.ApplicationProperties[MessageProperties.Poison] = true;

            await sender.SendMessageAsync(message);
            Console.WriteLine($"Published POISON message with MessageId={messageId}");

            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "poison-message-id.json"),
                JsonSerializer.Serialize(new { messageId }, new JsonSerializerOptions { WriteIndented = true }));
            break;
        }

    default:
        Console.WriteLine($"Unknown scenario '{scenario}'. Expected one of: batch, duplicate, poison.");
        break;
}
```

### Consumer source (`src/Consumer/Program.cs`)

```csharp
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
```

### Idempotency key handling (`src/Core/IdempotencyStore.cs`)

The dedupe key is the Service Bus `MessageId`. `TryMarkProcessed` does an atomic `INSERT OR IGNORE` keyed on that id into a SQLite table; it returns `true` only the first time a given id is seen, `false` on every repeat (whether that repeat is a genuine duplicate publish, a redelivery after an abandoned lock, or a different competing-consumer instance racing for the same message). Callers use the return value to decide whether to do real work, but always complete the message either way:

```csharp
public bool TryMarkProcessed(string messageId, string consumerInstance)
{
    lock (_lock)
    {
        using var insert = _connection.CreateCommand();
        insert.CommandText =
            """
            INSERT OR IGNORE INTO processed_messages (message_id, consumer_instance, processed_at_utc)
            VALUES ($id, $instance, $now);
            """;
        insert.Parameters.AddWithValue("$id", messageId);
        insert.Parameters.AddWithValue("$instance", consumerInstance);
        insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        var rowsInserted = insert.ExecuteNonQuery();
        return rowsInserted == 1;
    }
}
```

Used in the consumer's competing-consumer loop as:

```csharp
var wasNewProcessing = store.TryMarkProcessed(message.MessageId, instanceName);
// wasNewProcessing == false means: already handled (by this or another instance) — skip work, still complete().
```

This is proven directly, offline, by `tests/ServiceBusDemo.Tests/IdempotencyStoreTests.cs` and by the mutation check below.

### Proof a poison message landed in the DLQ

**Not available in this submission.** The live demo (which would have produced `output/dlq-evidence.json` with the broker's real `DeadLetterReason`) requires the Docker-based Service Bus emulator, and pulling its container images from `mcr.microsoft.com` stalled repeatedly on this network — confirmed as a genuine external throttling condition via direct `curl` diagnostics (DNS/TLS/API calls succeed instantly; sustained binary downloads crawl to a few KB/s and time out), not a local VPN/proxy misconfiguration. `README.md` has the exact reproduction steps (`dotnet run --project src/Publisher -- poison`, then `Consumer -- poison-handle 30`, then `Consumer -- dlq-read`) to produce this evidence the moment the pull succeeds. What **is** proven now, offline, is the mechanism that makes dead-lettering possible: `DeliveryTrackerTests.cs` proves the "exhausted after N attempts" logic deterministically, and `emulator/config.json` has `processing-sub` configured with `MaxDeliveryCount: 3`.

### Scope resolutions

- Event Grid: nothing was built for it — only mentioned in this line, since it's not in the actual brief/exercise text, only the page's topic label.
- Evidence source: **the local Azure Service Bus emulator** was the intended path (zero-cost, confirmed to functionally support everything this task needs) — but the live run itself could not be captured this session due to the network issue described above. No real Azure resource was created.

---

## What did you learn this session?

I learned that Service Bus topics really are two separate ideas stacked together: fan-out (multiple subscriptions each getting their own full copy) and competing consumers (multiple workers splitting one subscription), and that a poison message reaching the DLQ is just "the broker gives up after MaxDeliveryCount," not anything I have to code myself.

## What would break this?

Two consumer instances sharing one open SQLite connection without a lock around it would have let duplicate messages slip through under real concurrency — I caught that in review and fixed it before running anything live. Turning on the built-in `RequiresDuplicateDetection` instead of my own store would also quietly stop protecting against redeliveries, since that Service Bus feature only catches duplicate `MessageId`s within a short time window, not a message redelivered after my own handler crashed.
