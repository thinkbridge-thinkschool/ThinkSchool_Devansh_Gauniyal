using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.Ports;

// The InvoiceApproved integration event DESIGN.md names ("written to an outbox in
// the same transaction as approval, so a broker outage can never silently lose the
// fact that an invoice was approved") - for a Financing consumer that doesn't
// exist yet (see DESIGN.md's Financing context, "boundary only"). This port only
// ENQUEUES; it never publishes to Service Bus itself, and enqueuing must happen on
// the SAME DbContext instance ApproveInvoiceUseCase's own repository call uses, so
// the row lands in the same IUnitOfWork.SaveChangesAsync transaction as the
// invoice's own state change - see EfIntegrationEventOutbox for how that's
// guaranteed structurally rather than by convention. A separate
// OutboxRelayBackgroundService (host/Capstone.Web) is what actually publishes
// enqueued rows to Service Bus, on its own schedule, after the transaction that
// wrote them has already committed.
public interface IIntegrationEventOutbox
{
    Task EnqueueInvoiceApprovedAsync(InvoiceApproved domainEvent, CancellationToken cancellationToken);
}
