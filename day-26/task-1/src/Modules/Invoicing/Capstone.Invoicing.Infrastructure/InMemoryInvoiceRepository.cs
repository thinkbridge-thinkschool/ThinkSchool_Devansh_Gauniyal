using System.Collections.Concurrent;
using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Infrastructure;

// In-memory only - see the equivalent note on Procurement.Infrastructure's
// repository. Real persistence is Day 28+ work.
public sealed class InMemoryInvoiceRepository : IInvoiceRepository
{
    private readonly ConcurrentDictionary<InvoiceId, Invoice> _invoices = new();

    public Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(id));

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Invoice>> FindSubmittedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Invoice> submitted = [.. _invoices.Values.Where(i => i.Status == InvoiceStatus.Submitted)];
        return Task.FromResult(submitted);
    }
}
