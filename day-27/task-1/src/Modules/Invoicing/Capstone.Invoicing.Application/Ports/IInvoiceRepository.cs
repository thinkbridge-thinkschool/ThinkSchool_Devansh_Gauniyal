using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Application.Ports;

// A port the Invoicing module owns and Invoicing.Infrastructure implements - the
// standard "the consumer defines the interface" shape (dependency inversion), not
// "the database layer defines what a repository looks like".
public interface IInvoiceRepository
{
    Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken);

    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    // For the deemed-approval sweep (see ApplyDeemedApprovalsUseCase) - a real
    // implementation would filter this in the query, not in memory; an in-memory
    // scaffold repository is free to do it the simple way.
    Task<IReadOnlyList<Invoice>> FindSubmittedAsync(CancellationToken cancellationToken);

    // Day 27: backs GET /v1/invoices - added so the API hardening task (input
    // limits: page size) has a real, paged read endpoint to bound. skip/take are
    // already clamped by the caller; a real implementation would push both into
    // the query (OFFSET/FETCH), not page in memory.
    Task<IReadOnlyList<Invoice>> ListAsync(int skip, int take, CancellationToken cancellationToken);
}
