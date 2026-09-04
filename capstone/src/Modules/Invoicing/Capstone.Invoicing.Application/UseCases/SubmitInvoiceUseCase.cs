using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

// Orchestrates what a pure aggregate cannot do for itself: fetch the PO snapshot and
// the agreed terms, call Invoice.Submit(), persist the result, then react to the
// InvoiceSubmitted event by reserving PO capacity - synchronously, in this same
// call, because that reservation is a real invariant, not a side effect (see
// capstone/DESIGN.md, "async flows"). If ReserveAsync fails, the invoice that was
// just added should not be considered committed; a real implementation wraps this in
// a unit of work / database transaction spanning both repositories, which this
// in-memory scaffold does not attempt - see README.md, "what's deliberately not
// built yet".
public sealed class SubmitInvoiceUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    IPaymentTermsLookup paymentTerms,
    TimeProvider clock)
{
    public async Task<InvoiceId> ExecuteAsync(
        SubmitInvoiceCommand command,
        PurchaseOrderReference purchaseOrderId,
        MatchingPolicy matchingPolicy,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await purchaseOrderCapacity.GetSnapshotAsync(purchaseOrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Purchase order {purchaseOrderId} was not found.");

        var terms = await paymentTerms.GetAgreedTermsAsync(purchaseOrder.BuyerId, command.SupplierId, cancellationToken);

        var invoice = Invoice.Submit(command, purchaseOrder, terms, matchingPolicy, clock);

        await invoices.AddAsync(invoice, cancellationToken);

        foreach (var domainEvent in invoice.DomainEvents)
        {
            if (domainEvent is InvoiceSubmitted submitted)
            {
                await purchaseOrderCapacity.ReserveAsync(purchaseOrderId, submitted.ReservedAmount, cancellationToken);
            }
        }

        invoice.ClearDomainEvents();
        return invoice.Id;
    }
}
