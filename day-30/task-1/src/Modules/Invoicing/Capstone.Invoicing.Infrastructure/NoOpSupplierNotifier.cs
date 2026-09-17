using Capstone.Invoicing.Application.Ports;

namespace Capstone.Invoicing.Infrastructure;

// Local-dev fallback, registered only when no Service Bus namespace is
// configured (see Program.cs's `serviceBusConfigured` check) - the same
// pattern as NoOpIntegrationEventOutbox.
public sealed class NoOpSupplierNotifier : ISupplierNotifier
{
    public Task NotifyInvoiceApprovedAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task NotifyInvoiceDisputedAsync(Guid supplierId, string invoiceNumber, string reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
