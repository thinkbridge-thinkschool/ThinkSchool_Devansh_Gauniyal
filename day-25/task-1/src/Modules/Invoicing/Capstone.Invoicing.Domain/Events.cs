using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// One file for all five - each is a small fact, not a class hierarchy worth
// spreading across files yet. The application layer reacts to these after a use
// case completes (reserving/releasing/consuming PO capacity synchronously; see
// DESIGN.md's async-flows section for which reactions are NOT synchronous - supplier
// notification and the future InvoiceApproved integration event, neither of which
// exists as wired infrastructure in this scaffold).

public sealed record InvoiceSubmitted(InvoiceId InvoiceId, Money ReservedAmount, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InvoiceApproved(InvoiceId InvoiceId, ApprovalRecord Approval, Money ConsumedAmount, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InvoiceDisputed(InvoiceId InvoiceId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InvoiceRejected(InvoiceId InvoiceId, string Reason, Money ReleasedAmount, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InvoiceWithdrawn(InvoiceId InvoiceId, Money ReleasedAmount, DateTimeOffset OccurredAt) : IDomainEvent;
