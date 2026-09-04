namespace Capstone.Procurement.Domain;

// A wrapper around Guid rather than a bare Guid, so "which ID am I holding" is a
// compile-time question, not a runtime one - PurchaseOrderId and (Invoicing's)
// InvoiceId can never be accidentally swapped, because they're different types.
public readonly record struct PurchaseOrderId(Guid Value)
{
    public static PurchaseOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
