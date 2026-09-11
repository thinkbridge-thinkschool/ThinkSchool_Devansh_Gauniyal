using Capstone.SharedKernel;

namespace Capstone.Procurement.Domain;

// The Procurement side of the "invoiceable capacity" invariant (see
// capstone/DESIGN.md). A PO tracks three figures, not one: Total (what was
// authorised), Reserved (held by invoices still in flight - Submitted or Disputed,
// not yet resolved), and Consumed (locked in by Approved invoices). Collapsing this
// to a single "remaining" figure was the first design considered and rejected: it
// can't tell "money someone might still claim" apart from "money already owed",
// which is exactly the distinction that makes the reservation invariant meaningful.
//
// This aggregate does not know Invoicing exists. It exposes Reserve/Release/Consume
// as operations on amounts, called by Invoicing's own infrastructure adapter (see
// Capstone.Invoicing.Infrastructure/ProcurementCapacityAdapter.cs) through
// Procurement's own application-layer gateway - never a direct reference in either
// direction between the two modules' domains.
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>
{
    private readonly List<PurchaseOrderLine> _lines;

    private PurchaseOrder(
        PurchaseOrderId id,
        Guid vendorId,
        Guid buyerId,
        string currency,
        IReadOnlyCollection<PurchaseOrderLine> lines)
        : base(id)
    {
        VendorId = vendorId;
        BuyerId = buyerId;
        Currency = currency;
        _lines = [.. lines];
        Status = PurchaseOrderStatus.Open;
        Reserved = Money.Zero(currency);
        Consumed = Money.Zero(currency);
    }

    public Guid VendorId { get; }
    public Guid BuyerId { get; }
    public string Currency { get; }
    public PurchaseOrderStatus Status { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    public Money Total => _lines
        .Select(l => l.LineValue)
        .Aggregate(Money.Zero(Currency), (sum, line) => sum + line);

    public Money Reserved { get; private set; }
    public Money Consumed { get; private set; }

    // The figure Invoicing's submission check reads: what's left to reserve against.
    public Money Available => Total - Reserved - Consumed;

    public static PurchaseOrder Issue(
        PurchaseOrderId id, Guid vendorId, Guid buyerId, string currency, IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A purchase order must have at least one line.");
        }

        return new PurchaseOrder(id, vendorId, buyerId, currency, lines);
    }

    public void Reserve(Money amount)
    {
        EnsureOpen();
        if (amount > Available)
        {
            throw new InvalidOperationException(
                $"Cannot reserve {amount}: only {Available} of {Total} remains available on {Id}.");
        }

        Reserved += amount;
    }

    // Called when an invoice that was reserving capacity is rejected or withdrawn -
    // the amount was never actually owed, so it goes back to Available.
    public void ReleaseReservation(Money amount)
    {
        Reserved -= amount;
    }

    // Called when an invoice is approved: the amount stops being merely "possible"
    // and becomes an actual, locked liability against this order.
    public void ConsumeReservation(Money amount)
    {
        Reserved -= amount;
        Consumed += amount;
    }

    private void EnsureOpen()
    {
        if (Status != PurchaseOrderStatus.Open)
        {
            throw new InvalidOperationException($"Purchase order {Id} is not open (status: {Status}).");
        }
    }
}
