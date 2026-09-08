using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

// A buyer-initiated dispute on an otherwise-clean Submitted invoice. Does not touch
// PO capacity: the invoice was already reserving its amount from submission, and
// disputing it doesn't change that - only Reject or Withdraw releases the reservation.
public sealed class DisputeInvoiceUseCase(IInvoiceRepository invoices, TimeProvider clock)
{
    public async Task ExecuteAsync(InvoiceId invoiceId, string reason, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} was not found.");

        invoice.Dispute(reason, clock);
        invoice.ClearDomainEvents();
    }
}
