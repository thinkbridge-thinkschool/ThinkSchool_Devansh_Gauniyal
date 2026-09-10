using Capstone.Invoicing.Domain;
using Capstone.SharedKernel;

namespace Capstone.Invoicing.Application.Ports;

// The module-boundary port: Invoicing defines exactly what it needs from
// Procurement, in Invoicing's own vocabulary (PurchaseOrderSnapshot, not
// Procurement's PurchaseOrder aggregate). Implemented by
// Capstone.Invoicing.Infrastructure/ProcurementCapacityAdapter.cs, which is the
// ONLY place in the solution allowed to reference Procurement.Application - see
// capstone/README.md, "dependency direction".
public interface IPurchaseOrderCapacityPort
{
    Task<PurchaseOrderSnapshot?> GetSnapshotAsync(PurchaseOrderReference purchaseOrderId, CancellationToken cancellationToken);

    Task ReserveAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);

    Task ReleaseReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);

    Task ConsumeReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);
}
