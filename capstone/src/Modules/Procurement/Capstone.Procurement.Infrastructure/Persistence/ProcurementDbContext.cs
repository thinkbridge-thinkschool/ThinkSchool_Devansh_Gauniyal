using Capstone.Procurement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Procurement.Infrastructure.Persistence;

// The Procurement module's own EF Core context - maps only Procurement's aggregate
// (PurchaseOrder) into its own schema ("procurement", see
// PurchaseOrderEntityTypeConfiguration). Shares the physical Azure SQL database -
// and, within one request, the same underlying connection - with
// Capstone.Invoicing.Infrastructure.Persistence.InvoicingDbContext (see
// host/Capstone.Web's SharedTransactionUnitOfWork), but never references it or any
// Invoicing type - Procurement does not know Invoicing exists, in any layer,
// including this one. See capstone/README.md, "dependency rule, and how it's
// enforced".
public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PurchaseOrderEntityTypeConfiguration());
    }
}
