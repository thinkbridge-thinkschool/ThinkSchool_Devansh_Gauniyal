using System.Collections.Concurrent;
using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Infrastructure;

// In-memory only. Superseded at runtime by InvoiceEfRepository (Day 29's real
// persistence, see Persistence/InvoicingDbContext.cs) - kept in the solution rather
// than deleted, since tests/Capstone.ArchitectureTests still references this type
// to identify the Capstone.Invoicing.Infrastructure assembly, and a fast in-memory
// implementation may still be useful for tests that don't need a real database.
public sealed class InMemoryInvoiceRepository : IInvoiceRepository// contains code that performs ininvoicerepo
{
    private readonly ConcurrentDictionary<InvoiceId, Invoice> _invoices = new();

    public Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken) =>// returns invoice 
        Task.FromResult(_invoices.GetValueOrDefault(id));

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Invoice>> FindSubmittedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Invoice> submitted = [.. _invoices.Values.Where(i => i.Status == InvoiceStatus.Submitted)];// returns only the submitted invoices 
        return Task.FromResult(submitted);
    }

    public Task<IReadOnlyList<Invoice>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<Invoice> page = [.. _invoices.Values.OrderBy(i => i.Id.Value).Skip(skip).Take(take)];
        return Task.FromResult(page);
    }
}
