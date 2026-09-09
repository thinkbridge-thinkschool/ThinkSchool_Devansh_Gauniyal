using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Domain;
using Capstone.Procurement.Application;
using Capstone.SharedKernel;

namespace Capstone.Invoicing.Infrastructure;

// The ONE place in the whole solution that references both modules. It implements
// Invoicing's own port (IPurchaseOrderCapacityPort, defined in
// Capstone.Invoicing.Application) by translating to and from Procurement's published
// contract (IPurchaseOrderCapacityGateway, in Capstone.Procurement.Application) -
// converting between Invoicing's PurchaseOrderReference/PurchaseOrderSnapshot and
// Procurement's Guid/PurchaseOrderCapacitySnapshot. Neither module's Domain project
// is referenced here, and neither module's Domain project references the other -
// see capstone/README.md, "dependency direction", for what that buys.
public sealed class ProcurementCapacityAdapter(IPurchaseOrderCapacityGateway procurement) : IPurchaseOrderCapacityPort
{
    public async Task<PurchaseOrderSnapshot?> GetSnapshotAsync(
        PurchaseOrderReference purchaseOrderId, CancellationToken cancellationToken)
    {
        var snapshot = await procurement.GetSnapshotAsync(purchaseOrderId.Value, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return new PurchaseOrderSnapshot(
            new PurchaseOrderReference(snapshot.PurchaseOrderId),
            snapshot.VendorId,
            snapshot.BuyerId,
            snapshot.Currency,
            snapshot.IsOpen,
            snapshot.Available,
            [.. snapshot.Lines.Select(l => new PurchaseOrderLineSnapshot(l.LineNumber, l.LineValue))]);
    }

    public Task ReserveAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken) =>
        procurement.ReserveAsync(purchaseOrderId.Value, amount, cancellationToken);

    public Task ReleaseReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken) =>
        procurement.ReleaseReservationAsync(purchaseOrderId.Value, amount, cancellationToken);

    public Task ConsumeReservationAsync(PurchaseOrderReference purchaseOrderId, Money amount, CancellationToken cancellationToken) =>
        procurement.ConsumeReservationAsync(purchaseOrderId.Value, amount, cancellationToken);
}
