namespace Capstone.Web.Persistence;

// The one place, outside either module, that knows persisting an invoice
// submission or approval might touch BOTH Invoicing's and Procurement's tables.
// Application-layer use cases (SubmitInvoiceUseCase, ApproveInvoiceUseCase, ...)
// stay completely unaware this exists - they call repository methods that only
// stage changes on a tracked DbContext; an endpoint handler in this host commits
// them, once, after the use case returns. See SharedTransactionUnitOfWork.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

// Local-dev fallback for when no real database is configured (see Program.cs's
// `persistenceConfigured` check) - the in-memory repositories mutate their
// ConcurrentDictionary immediately, so there is nothing left to commit.
public sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
