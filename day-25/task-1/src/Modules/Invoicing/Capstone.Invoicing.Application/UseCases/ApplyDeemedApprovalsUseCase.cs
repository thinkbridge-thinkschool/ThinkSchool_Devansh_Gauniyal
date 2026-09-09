using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

// The deemed-approval SLA sweep (see capstone/DESIGN.md). Deliberately a plain,
// callable use case, not a background service or a hosted timer: a scheduled sweep
// evaluating a deadline is a fundamentally different mechanism from an event queue
// (see README.md, "what's deliberately not built yet" - conflating the two was one
// of the anti-patterns flagged while designing this). Something would call this
// once a day in a built system; nothing calls it automatically here.
public sealed class ApplyDeemedApprovalsUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<InvoiceId>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var deemed = new List<InvoiceId>();

        foreach (var invoice in await invoices.FindSubmittedAsync(cancellationToken))
        {
            var deadline = invoice.SubmittedAt.AddDays(invoice.Terms.ReviewWindowDays);
            if (clock.GetUtcNow() < deadline)
            {
                continue;
            }

            invoice.ApplyDeemedApproval(clock);

            foreach (var domainEvent in invoice.DomainEvents)
            {
                if (domainEvent is InvoiceApproved approved)
                {
                    await purchaseOrderCapacity.ConsumeReservationAsync(
                        invoice.PurchaseOrderId, approved.ConsumedAmount, cancellationToken);
                }
            }

            invoice.ClearDomainEvents();
            deemed.Add(invoice.Id);
        }

        return deemed;
    }
}
