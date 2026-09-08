using Capstone.SharedKernel;

namespace Capstone.Procurement.Application;

// The published contract Procurement exposes to other modules - deliberately its own
// type, not Procurement.Domain.PurchaseOrder itself. A consumer (Invoicing's
// adapter) only ever sees this shape, never Procurement's aggregate, so Procurement
// is free to change its internal model without breaking anything that reads this.
public sealed record PurchaseOrderCapacitySnapshot(
    Guid PurchaseOrderId,
    Guid VendorId,
    Guid BuyerId,
    string Currency,
    bool IsOpen,
    Money Available,
    IReadOnlyCollection<PurchaseOrderLineCapacity> Lines);

public sealed record PurchaseOrderLineCapacity(int LineNumber, Money LineValue);
