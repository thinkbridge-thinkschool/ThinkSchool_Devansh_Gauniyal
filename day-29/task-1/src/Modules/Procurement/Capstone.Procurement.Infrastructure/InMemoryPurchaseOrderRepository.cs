using System.Collections.Concurrent;
using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;

namespace Capstone.Procurement.Infrastructure;

// In-memory only. Superseded at runtime by PurchaseOrderEfRepository (Day 29's real
// persistence, see Persistence/ProcurementDbContext.cs) - kept in the solution
// rather than deleted, since tests/Capstone.ArchitectureTests still references this
// type to identify the Capstone.Procurement.Infrastructure assembly, and a fast
// in-memory implementation may still be useful for tests that don't need a real
// database. Stores the aggregate by reference, so a mutation made through a
// fetched PurchaseOrder is already visible to the next fetch - the real EF Core
// implementation instead relies on the change tracker plus an explicit
// SaveChangesAsync (see Capstone.Web's SharedTransactionUnitOfWork).
public sealed class InMemoryPurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly ConcurrentDictionary<PurchaseOrderId, PurchaseOrder> _purchaseOrders = new();

    public Task<PurchaseOrder?> FindAsync(PurchaseOrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(_purchaseOrders.GetValueOrDefault(id));

    public Task AddAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken)
    {
        _purchaseOrders[purchaseOrder.Id] = purchaseOrder;
        return Task.CompletedTask;
    }
}
