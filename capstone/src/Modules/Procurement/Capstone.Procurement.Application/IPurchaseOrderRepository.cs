using Capstone.Procurement.Domain;

namespace Capstone.Procurement.Application;

// Procurement's own repository port - implemented by Capstone.Procurement.Infrastructure.
public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> FindAsync(PurchaseOrderId id, CancellationToken cancellationToken);

    Task AddAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken);
}
