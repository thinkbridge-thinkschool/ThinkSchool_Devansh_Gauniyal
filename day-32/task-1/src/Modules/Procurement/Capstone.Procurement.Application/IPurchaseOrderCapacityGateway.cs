using Capstone.SharedKernel;

namespace Capstone.Procurement.Application;

// Procurement's own public-facing contract for "what another module may do to a
// purchase order's capacity" - the ONLY thing outside this module is allowed to
// touch. Capstone.Invoicing.Infrastructure references this interface (and this
// project) to implement Invoicing's own IPurchaseOrderCapacityPort; nothing outside
// this module ever sees Capstone.Procurement.Domain.
public interface IPurchaseOrderCapacityGateway
{
    Task<PurchaseOrderCapacitySnapshot?> GetSnapshotAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    Task ReserveAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken);

    Task ReleaseReservationAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken);

    Task ConsumeReservationAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken);
}
