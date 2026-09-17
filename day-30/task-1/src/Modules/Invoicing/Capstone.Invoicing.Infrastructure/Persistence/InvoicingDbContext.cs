using Capstone.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Invoicing.Infrastructure.Persistence;

// The Invoicing module's own EF Core context - maps only Invoicing's aggregate
// (Invoice) into its own schema ("invoicing", see InvoiceEntityTypeConfiguration).
// Shares the physical Azure SQL database - and, within one request, the same
// underlying connection - with Capstone.Procurement.Infrastructure.Persistence.
// ProcurementDbContext (see host/Capstone.Web's SharedTransactionUnitOfWork), but
// never references it or any Procurement type. The module boundary holds at the
// persistence layer exactly as it does everywhere else in this solution - see
// capstone/README.md, "dependency rule, and how it's enforced".
public sealed class InvoicingDbContext(DbContextOptions<InvoicingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    // Day 30 - the transactional outbox for the InvoiceApproved integration
    // event (see OutboxMessage.cs). Lives on this context, not a separate one,
    // specifically so writing a row here shares the same transaction as the
    // Invoice write that triggered it (see EfIntegrationEventOutbox).
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InvoiceEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
    }
}
