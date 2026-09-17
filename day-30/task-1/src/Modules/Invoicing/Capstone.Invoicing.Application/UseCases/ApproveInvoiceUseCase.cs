using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

public sealed class ApproveInvoiceUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    IIntegrationEventOutbox outbox,
    ISupplierNotifier supplierNotifier,
    TimeProvider clock)
{
    public async Task ExecuteAsync(InvoiceId invoiceId, Guid approvingActorId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} was not found.");

        invoice.Approve(approvingActorId, clock);

        // Reservation becomes consumption the moment the invoice is a locked
        // liability - see PurchaseOrder.ConsumeReservation and DESIGN.md.
        foreach (var domainEvent in invoice.DomainEvents)
        {
            if (domainEvent is InvoiceApproved approved)
            {
                await purchaseOrderCapacity.ConsumeReservationAsync(
                    invoice.PurchaseOrderId, approved.ConsumedAmount, cancellationToken);

                // Day 30 - the two genuinely async flows DESIGN.md names. The
                // outbox write happens on the SAME DbContext/transaction as
                // everything above (see EfIntegrationEventOutbox) - a broker
                // outage can never lose the fact that this invoice was approved.
                // The notification is deliberately NOT part of that guarantee -
                // see ISupplierNotifier's comment for why a failure here must
                // never fail this request.
                await outbox.EnqueueInvoiceApprovedAsync(approved, cancellationToken);
            }
        }

        invoice.ClearDomainEvents();

        await supplierNotifier.NotifyInvoiceApprovedAsync(invoice.SupplierId, invoice.InvoiceNumber, cancellationToken);
    }
}
