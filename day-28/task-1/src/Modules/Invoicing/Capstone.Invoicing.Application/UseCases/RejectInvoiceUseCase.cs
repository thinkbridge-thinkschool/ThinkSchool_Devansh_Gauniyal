using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

// The correction path (see capstone/DESIGN.md): rejecting a disputed invoice
// releases the PO capacity it was holding. Creating the replacement corrective
// invoice (CorrectsInvoiceId) is a separate, later call to SubmitInvoiceUseCase by
// whoever orchestrates the correction - this use case only ever closes the rejected
// one out.
public sealed class RejectInvoiceUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    TimeProvider clock)
{
    public async Task ExecuteAsync(InvoiceId invoiceId, string reason, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, cancellationToken)// calls the in memory invoice repo 
            ?? throw new InvalidOperationException($"Invoice {invoiceId} was not found.");

        invoice.Reject(reason, clock);// calls invoice.cs for rejecting the invoice

        foreach (var domainEvent in invoice.DomainEvents)
        {
            if (domainEvent is InvoiceRejected rejected)
            {
                await purchaseOrderCapacity.ReleaseReservationAsync(// pases the request to procurement adapter 
                    invoice.PurchaseOrderId, rejected.ReleasedAmount, cancellationToken);
            }
        }

        invoice.ClearDomainEvents();
    }
}
