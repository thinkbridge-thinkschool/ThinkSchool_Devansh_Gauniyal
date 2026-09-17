using Capstone.Invoicing.Domain;
using Capstone.SharedKernel;

namespace Capstone.Invoicing.Application.Ports;

// The module-boundary port: Invoicing defines exactly what it needs from
// Procurement, in Invoicing's own vocabulary (PurchaseOrderSnapshot, not
// Procurement's PurchaseOrder aggregate). Implemented by
// Capstone.Invoicing.Infrastructure/ProcurementCapacityAdapter.cs, which is the
// ONLY place in the solution allowed to reference Procurement.Application - see
// capstone/README.md, "dependency direction".
public interface IPurchaseOrderCapacityPort// is a interface that promises these tasks would be provided by the class
{
    Task<PurchaseOrderSnapshot?> GetSnapshotAsync(PurchaseOrderReference purchaseOrderId, CancellationToken cancellationToken);// get P/O details

    Task ReserveAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);//hold amount for invoice 

    Task ReleaseReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);//release the held amount 

    Task ConsumeReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken);// move held amount to consumed
}
// this file helps use cases ask for this code without containing code that connects to procurement 
//(look up in adapter)