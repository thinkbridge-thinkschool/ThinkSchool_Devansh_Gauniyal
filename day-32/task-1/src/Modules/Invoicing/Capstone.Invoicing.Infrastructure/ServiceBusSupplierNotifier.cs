using Azure.Messaging.ServiceBus;
using Capstone.Invoicing.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Capstone.Invoicing.Infrastructure;

// Best-effort, deliberately not transactional - see ISupplierNotifier's comment
// for why. Every send is wrapped so an exception here (Service Bus unreachable,
// misconfigured topic, whatever) is logged and swallowed, never rethrown - a
// failed notification must never fail the approve/dispute request it's attached
// to, which is the entire reason DESIGN.md classifies this as a convenience
// rather than a correctness dependency.
public sealed class ServiceBusSupplierNotifier(
    ServiceBusClient client,
    IConfiguration configuration,
    ILogger<ServiceBusSupplierNotifier> logger) : ISupplierNotifier
{
    public Task NotifyInvoiceApprovedAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken) =>
        SendAsync(supplierId, new { eventType = "InvoiceApproved", invoiceNumber }, cancellationToken);

    public Task NotifyInvoiceDisputedAsync(Guid supplierId, string invoiceNumber, string reason, CancellationToken cancellationToken) =>
        SendAsync(supplierId, new { eventType = "InvoiceDisputed", invoiceNumber, reason }, cancellationToken);

    private async Task SendAsync(Guid supplierId, object payload, CancellationToken cancellationToken)
    {
        try
        {
            var topicName = configuration["ServiceBus:SupplierNotificationsTopicName"]
                ?? throw new InvalidOperationException("ServiceBus:SupplierNotificationsTopicName is not configured.");

            await using var sender = client.CreateSender(topicName);
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(payload))
            {
                // A real notification service would route on this (email/SMS/portal
                // alert to the specific supplier) - no such consumer exists yet, per
                // DESIGN.md; this is the field it would filter on.
                Subject = supplierId.ToString(),
            };

            await sender.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Supplier notification to {SupplierId} failed; continuing without it.", supplierId);
        }
    }
}
