using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.Ports;

// Payment Terms is reference data for this slice, not a bounded context - see
// capstone/DESIGN.md. This port is deliberately tiny (one read method) because
// that's genuinely all Invoicing needs today: a term length and a review-window SLA,
// looked up ONCE at submission and then snapshotted onto the invoice forever (see
// PaymentTermsSnapshot.cs). If this ever grows a write side, a negotiation
// workflow, or versioning, that's the signal it has become its own context.
public interface IPaymentTermsLookup
{
    Task<PaymentTermsSnapshot> GetAgreedTermsAsync(Guid buyerId, Guid supplierId, CancellationToken cancellationToken);
}
