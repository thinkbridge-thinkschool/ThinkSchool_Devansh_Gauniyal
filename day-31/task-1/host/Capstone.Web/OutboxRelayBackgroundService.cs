using Azure.Messaging.ServiceBus;
using Capstone.Invoicing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Web;

// Day 30 - the relay half of DESIGN.md's InvoiceApproved outbox: publishes rows
// EfIntegrationEventOutbox already wrote (transactionally, with the approval
// itself - see that type's comment) to Service Bus, on its own schedule,
// independent of the request that wrote them. This is the piece that makes the
// outbox pattern actually work end to end - a row sitting unpublished forever
// would be no better than not writing it at all.
//
// This is new code inside Capstone.Web, not a repurposed
// host/Capstone.Worker/TraceDemoWorker.cs - see
// submission-day-30-task-1.md for why: TraceDemoWorker still serves its own,
// separate, still-valid purpose (proving distributed tracing propagation
// across a Service Bus hop), and Capstone.Web already has every piece this
// relay needs (the database connection, the Service Bus client, the managed
// identity) wired and deployed, so adding it here needed no new infrastructure
// and no second app's redeploy to get this flow running today.
public sealed class OutboxRelayBackgroundService(
    IServiceScopeFactory scopeFactory,
    ServiceBusClient serviceBusClient,
    IConfiguration configuration,
    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = double.TryParse(configuration["OutboxRelay:IntervalSeconds"], out var configured)
            ? configured
            : 10;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        do
        {
            try
            {
                await RelayPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox relay tick failed; will retry on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RelayPendingMessagesAsync(CancellationToken cancellationToken)
    {
        var topicName = configuration["ServiceBus:InvoiceApprovedEventsTopicName"]
            ?? throw new InvalidOperationException("ServiceBus:InvoiceApprovedEventsTopicName is not configured.");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        await using var sender = serviceBusClient.CreateSender(topicName);
        var publishedAt = DateTimeOffset.UtcNow;

        foreach (var message in pending)
        {
            var serviceBusMessage = new ServiceBusMessage(message.Payload)
            {
                MessageId = message.Id.ToString(),
                Subject = message.EventType,
            };
            await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
            message.PublishedAt = publishedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Outbox relay published {Count} message(s) to {Topic}.", pending.Count, topicName);
    }
}
