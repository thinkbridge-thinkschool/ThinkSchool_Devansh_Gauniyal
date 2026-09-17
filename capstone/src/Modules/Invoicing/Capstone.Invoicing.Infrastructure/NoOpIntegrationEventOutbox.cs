using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;

namespace Capstone.Invoicing.Infrastructure;

// Local-dev fallback, same reasoning as InMemoryInvoiceRepository - registered
// only when no real database is configured (see Program.cs's
// `persistenceConfigured` check), since there is no InvoicingDbContext for
// EfIntegrationEventOutbox to write into.
public sealed class NoOpIntegrationEventOutbox : IIntegrationEventOutbox
{
    public Task EnqueueInvoiceApprovedAsync(InvoiceApproved domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
