using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

public sealed class ApproveInvoiceUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
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
            }
        }

        invoice.ClearDomainEvents();

        // What would happen next in a built system, not simulated here (see
        // DESIGN.md "async flows" and README.md "what's deliberately not built
        // yet"): a supplier notification, and an InvoiceApproved integration event
        // written to an outbox for a future Financing consumer.
    }
}
