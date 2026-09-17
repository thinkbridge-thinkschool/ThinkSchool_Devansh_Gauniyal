using System.Text.Json;
using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;
using Capstone.Invoicing.Infrastructure.Persistence;

namespace Capstone.Invoicing.Infrastructure;

// Writes to the SAME InvoicingDbContext instance ApproveInvoiceUseCase's own
// IInvoiceRepository call uses within a request - that's what makes the outbox
// row part of the same IUnitOfWork.SaveChangesAsync transaction as the invoice's
// own Approved status, not a separate guarantee this class has to manage itself.
// Deliberately does NOT call SaveChangesAsync, same reasoning as
// InvoiceEfRepository - see that type's comment.
public sealed class EfIntegrationEventOutbox(InvoicingDbContext dbContext) : IIntegrationEventOutbox
{
    public async Task EnqueueInvoiceApprovedAsync(InvoiceApproved domainEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new InvoiceApprovedIntegrationEvent(
            domainEvent.InvoiceId.Value,
            domainEvent.Approval.Kind.ToString(),
            domainEvent.Approval.ApprovedBy,
            domainEvent.ConsumedAmount.Amount,
            domainEvent.ConsumedAmount.Currency,
            domainEvent.OccurredAt));

        await dbContext.OutboxMessages.AddAsync(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = "InvoiceApproved",
                Payload = payload,
                OccurredAt = domainEvent.OccurredAt,
            },
            cancellationToken);
    }
}

// The wire shape published to Service Bus - deliberately its own type, not the
// domain's InvoiceApproved event serialized directly. A future Financing
// consumer (see DESIGN.md) should never be coupled to this module's internal
// domain-event shape; this is the actual published contract, versioned and
// changed independently of Invoice.cs's own events.
public sealed record InvoiceApprovedIntegrationEvent(
    Guid InvoiceId,
    string ApprovalKind,
    Guid? ApprovedBy,
    decimal ConsumedAmount,
    string Currency,
    DateTimeOffset ApprovedAt);
