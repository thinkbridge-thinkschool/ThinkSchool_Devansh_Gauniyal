using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

public sealed class WithdrawInvoiceUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    TimeProvider clock)
{
    public async Task ExecuteAsync(InvoiceId invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} was not found.");

        invoice.Withdraw(clock);

        foreach (var domainEvent in invoice.DomainEvents)
        {
            if (domainEvent is InvoiceWithdrawn withdrawn)
            {
                await purchaseOrderCapacity.ReleaseReservationAsync(
                    invoice.PurchaseOrderId, withdrawn.ReleasedAmount, cancellationToken);
            }
        }

        invoice.ClearDomainEvents();
    }
}
