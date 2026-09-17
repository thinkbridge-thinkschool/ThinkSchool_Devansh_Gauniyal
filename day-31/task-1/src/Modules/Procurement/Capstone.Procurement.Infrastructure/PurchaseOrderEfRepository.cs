using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;
using Capstone.Procurement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Procurement.Infrastructure;

// Real persistence (Day 29) - see InMemoryPurchaseOrderRepository.cs's comment for
// the scaffold this replaces at runtime (kept in the solution, not deleted; still
// referenced by tests/Capstone.ArchitectureTests). Like InvoiceEfRepository, this
// deliberately never calls SaveChangesAsync itself - Capstone.Web's
// SharedTransactionUnitOfWork commits both modules' tracked changes together, in
// one transaction, once per request.
public sealed class PurchaseOrderEfRepository(ProcurementDbContext dbContext) : IPurchaseOrderRepository
{
    public Task<PurchaseOrder?> FindAsync(PurchaseOrderId id, CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task AddAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken) =>
        await dbContext.PurchaseOrders.AddAsync(purchaseOrder, cancellationToken);
}
