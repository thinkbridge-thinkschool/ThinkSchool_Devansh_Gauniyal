using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;
using Capstone.Invoicing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Invoicing.Infrastructure;

// Real persistence (Day 29) - see InMemoryInvoiceRepository.cs's comment for the
// scaffold this replaces at runtime (kept in the solution, not deleted; still
// referenced by tests/Capstone.ArchitectureTests). Deliberately does NOT call
// SaveChangesAsync itself - AddAsync only stages the insert on the tracked change
// set; Capstone.Web's SharedTransactionUnitOfWork commits it, together with
// whatever Procurement.Infrastructure staged on its own context in the same
// request, as one atomic transaction. A repository calling SaveChanges on its own
// would defeat that - see DESIGN.md's "transactionally consistent" reasoning for
// why this matters here specifically (PO capacity reservation must not race).
public sealed class InvoiceEfRepository(InvoicingDbContext dbContext) : IInvoiceRepository
{
    public Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken) =>
        dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken) =>
        await dbContext.Invoices.AddAsync(invoice, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> FindSubmittedAsync(CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .Where(i => i.Status == InvoiceStatus.Submitted)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListAsync(int skip, int take, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .OrderBy(i => i.SubmittedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
}
