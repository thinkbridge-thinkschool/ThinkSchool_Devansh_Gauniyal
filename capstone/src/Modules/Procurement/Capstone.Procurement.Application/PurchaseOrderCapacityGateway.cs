using Capstone.Procurement.Domain;
using Capstone.SharedKernel;

namespace Capstone.Procurement.Application;

public sealed class PurchaseOrderCapacityGateway(IPurchaseOrderRepository purchaseOrders) : IPurchaseOrderCapacityGateway
{
    public async Task<PurchaseOrderCapacitySnapshot?> GetSnapshotAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var purchaseOrder = await purchaseOrders.FindAsync(new PurchaseOrderId(purchaseOrderId), cancellationToken);
        return purchaseOrder is null ? null : ToSnapshot(purchaseOrder);
    }

    public async Task ReserveAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken)
    {
        var purchaseOrder = await RequireAsync(purchaseOrderId, cancellationToken);
        purchaseOrder.Reserve(amount);
    }

    public async Task ReleaseReservationAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken)
    {
        var purchaseOrder = await RequireAsync(purchaseOrderId, cancellationToken);
        purchaseOrder.ReleaseReservation(amount);
    }

    public async Task ConsumeReservationAsync(Guid purchaseOrderId, Money amount, CancellationToken cancellationToken)
    {
        var purchaseOrder = await RequireAsync(purchaseOrderId, cancellationToken);
        purchaseOrder.ConsumeReservation(amount);
    }

    private async Task<PurchaseOrder> RequireAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        await purchaseOrders.FindAsync(new PurchaseOrderId(purchaseOrderId), cancellationToken)
        ?? throw new InvalidOperationException($"Purchase order {purchaseOrderId} was not found.");

    private static PurchaseOrderCapacitySnapshot ToSnapshot(PurchaseOrder purchaseOrder) => new(
        purchaseOrder.Id.Value,
        purchaseOrder.VendorId,
        purchaseOrder.BuyerId,
        purchaseOrder.Currency,
        purchaseOrder.Status == PurchaseOrderStatus.Open,
        purchaseOrder.Available,
        [.. purchaseOrder.Lines.Select(l => new PurchaseOrderLineCapacity(l.LineNumber, l.LineValue))]);
}
