using System.Collections.Concurrent;
using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;

namespace Capstone.Procurement.Infrastructure;

// In-memory only, for this scaffold - no database, no migrations (see
// capstone/README.md, "what's deliberately not built yet"). Stores the aggregate by
// reference, so a mutation made through a fetched PurchaseOrder is already visible
// to the next fetch; a real (EF Core or similar) implementation would need an
// explicit save/unit-of-work call that this in-memory version has no need for.
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
