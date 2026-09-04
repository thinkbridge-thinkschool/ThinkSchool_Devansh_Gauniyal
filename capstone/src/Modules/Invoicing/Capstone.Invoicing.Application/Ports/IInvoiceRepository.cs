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
}
