using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Infrastructure;

// Payment Terms is reference data for this slice, not a bounded context (see
// capstone/DESIGN.md) - so this is a flat lookup with a single fallback default,
// not a negotiated-terms store. Real terms data (per buyer/supplier relationship) is
// Day 28+ work; this exists so IPaymentTermsLookup has a real implementation to
// wire, not just an interface nobody can run.
public sealed class InMemoryPaymentTermsLookup : IPaymentTermsLookup
{
    private static readonly PaymentTermsSnapshot DefaultTerms = new(netDays: 45, reviewWindowDays: 10);

    public Task<PaymentTermsSnapshot> GetAgreedTermsAsync(Guid buyerId, Guid supplierId, CancellationToken cancellationToken) =>
        Task.FromResult(DefaultTerms);
}
