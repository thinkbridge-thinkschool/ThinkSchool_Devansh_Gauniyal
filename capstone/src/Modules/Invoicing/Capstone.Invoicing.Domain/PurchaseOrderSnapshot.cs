using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Invoicing's own read-only view of "enough about a purchase order to validate an
// invoice against it" - assembled by the application layer (via
// IPurchaseOrderCapacityPort) from Procurement's data, never Procurement's actual
// PurchaseOrder aggregate passed across the boundary. Invoicing.Domain has no way to
// construct one of Procurement's PurchaseOrder objects and never sees one.
public sealed record PurchaseOrderSnapshot(
    PurchaseOrderReference Id,
    Guid VendorId,
    Guid BuyerId,
    string Currency,
    bool IsOpen,
    Money Available,
    IReadOnlyCollection<PurchaseOrderLineSnapshot> Lines);

public sealed record PurchaseOrderLineSnapshot(int LineNumber, Money LineValue);
