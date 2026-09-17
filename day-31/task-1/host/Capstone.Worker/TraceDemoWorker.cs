using System.Diagnostics;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;

namespace Capstone.Worker;

/// <summary>
/// Day 26 trace-demo worker - NOT the real async flows from capstone/DESIGN.md
/// ("supplier notification", the InvoiceApproved integration event). Both of
/// those remain exactly as unbuilt as they were through Day 25. This class exists
/// solely to consume one Service Bus message and write one SQL row, so a
/// distributed trace has a genuine API -> worker -> DB path to stitch across. See
/// capstone/infra/README.md, "Day 26 - the trace-demo worker".
/// </summary>
public sealed class TraceDemoWorker(
    IConfiguration configuration,
    TokenCredential credential,
    ILogger<TraceDemoWorker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Capstone.Worker");

    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
            ?? throw new InvalidOperationException("ServiceBus:FullyQualifiedNamespace is not configured.");
        var topicName = configuration["ServiceBus:DemoSubscriptionTopicName"]
            ?? throw new InvalidOperationException("ServiceBus:DemoSubscriptionTopicName is not configured.");
        var subscriptionName = configuration["ServiceBus:DemoSubscriptionName"]
            ?? throw new InvalidOperationException("ServiceBus:DemoSubscriptionName is not configured.");
        var connectionString = configuration.GetConnectionString("CapstoneDb")
            ?? throw new InvalidOperationException("ConnectionStrings:CapstoneDb is not configured.");

        _client = new ServiceBusClient(fullyQualifiedNamespace, credential);
        _processor = _client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions());

        _processor.ProcessMessageAsync += args => ProcessMessageAsync(args, connectionString);
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Trace-demo worker Service Bus error in {ErrorSource}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation(
            "Trace-demo worker listening on {Topic}/{Subscription}", topicName, subscriptionName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args, string sqlConnectionString)
    {
        // Day 26: the explicit propagation step this whole worker exists to prove -
        // see host/Capstone.Web/Program.cs's /demo/trace-worker endpoint for the
        // sender side. Service Bus carries no context on its own; whatever the
        // sender didn't put into ApplicationProperties is gone by the time this
        // callback runs. ActivityContext.TryParse understands the W3C traceparent
        // format Activity.Id already produces.
        var hasParentContext = args.Message.ApplicationProperties.TryGetValue("traceparent", out var traceparentObj)
            && traceparentObj is string traceparent
            && ActivityContext.TryParse(traceparent, null, out var parentContext);

        using var activity = hasParentContext
            ? ActivitySource.StartActivity("capstone-worker.process-trace-demo-message", ActivityKind.Consumer, parentContext)
            : ActivitySource.StartActivity("capstone-worker.process-trace-demo-message", ActivityKind.Consumer);

        var messageId = args.Message.MessageId;
        logger.LogInformation(
            "Trace-demo worker received message {MessageId}, parent context found: {HasParentContext}",
            messageId, hasParentContext);

        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.OpenAsync(args.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.TraceDemoEvents (TraceId, MessageId, ProcessedAtUtc)
            VALUES (@TraceId, @MessageId, @ProcessedAtUtc)
            """;
        command.Parameters.AddWithValue("@TraceId", activity?.TraceId.ToString() ?? "none");
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@ProcessedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(args.CancellationToken);

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);

        logger.LogInformation(
            "Trace-demo worker completed message {MessageId} with trace id {TraceId}",
            messageId, activity?.TraceId.ToString() ?? "none");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
