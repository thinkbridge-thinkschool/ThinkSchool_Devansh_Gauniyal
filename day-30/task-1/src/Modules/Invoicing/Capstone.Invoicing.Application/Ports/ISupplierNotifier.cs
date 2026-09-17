namespace Capstone.Invoicing.Application.Ports;

// The other genuinely async flow DESIGN.md names: "Supplier notification on
// approval/dispute - failure doesn't affect correctness; the invoice is the
// source of truth, the notification a convenience, retried independently."
// Unlike IIntegrationEventOutbox, this is NOT transactionally guaranteed and
// deliberately so - a real implementation (see ServiceBusSupplierNotifier) must
// never let a notification failure fail the approve/dispute request it's
// attached to. "Retried independently" here means by the underlying transport's
// own delivery guarantees (Service Bus), not by this project re-queuing a failed
// send itself - a fuller retry policy is real, unbuilt work, named honestly
// rather than assumed away.
public interface ISupplierNotifier
{
    Task NotifyInvoiceApprovedAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken);

    Task NotifyInvoiceDisputedAsync(Guid supplierId, string invoiceNumber, string reason, CancellationToken cancellationToken);
}
