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
