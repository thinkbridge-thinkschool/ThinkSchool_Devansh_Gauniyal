using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.UseCases;

// The deemed-approval SLA sweep (see capstone/DESIGN.md). Deliberately a plain,
// callable use case, not a background service or a hosted timer itself: a
// scheduled sweep evaluating a deadline is a fundamentally different mechanism
// from an event queue (see README.md - conflating the two was one of the
// anti-patterns flagged while designing this), so the SCHEDULING lives one layer
// up, not here. Day 30: something now actually calls this on a real interval -
// see host/Capstone.Web/DeemedApprovalSweepBackgroundService.cs - closing the
// "abandoned Submitted invoice ties up capacity indefinitely" gap DESIGN.md
// names, for the Submitted case (a Disputed invoice awaiting bilateral
// resolution is a different, still-open problem - see
// submission-day-30-task-1.md for why that one isn't solved the same way).
public sealed class ApplyDeemedApprovalsUseCase(
    IInvoiceRepository invoices,
    IPurchaseOrderCapacityPort purchaseOrderCapacity,
    IIntegrationEventOutbox outbox,
    ISupplierNotifier supplierNotifier,
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

            // Same reaction as ApproveInvoiceUseCase's human-approval path -
            // "approved" doesn't distinguish how, per DESIGN.md's
            // Human/DeemedBySla split existing for audit purposes, not to give
            // deemed approval a different downstream effect.
            foreach (var domainEvent in invoice.DomainEvents)
            {
                if (domainEvent is InvoiceApproved approved)
                {
                    await purchaseOrderCapacity.ConsumeReservationAsync(
                        invoice.PurchaseOrderId, approved.ConsumedAmount, cancellationToken);
                    await outbox.EnqueueInvoiceApprovedAsync(approved, cancellationToken);
                }
            }

            invoice.ClearDomainEvents();
            await supplierNotifier.NotifyInvoiceApprovedAsync(invoice.SupplierId, invoice.InvoiceNumber, cancellationToken);
            deemed.Add(invoice.Id);
        }

        return deemed;
    }
}
