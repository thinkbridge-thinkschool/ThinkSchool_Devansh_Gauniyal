using Capstone.SharedKernel;

namespace Capstone.Procurement.Domain;

// Owned by PurchaseOrder, never referenced independently - a PO line has no meaning
// or identity outside its order. LineNumber (not a database-generated ID) is what
// Invoicing's line-level matching refers back to.
public sealed record PurchaseOrderLine(int LineNumber, string ItemReference, int OrderedQuantity, Money UnitPrice)
{
    // EF Core materialization only - see InvoiceLineItem.cs's identical
    // constructor for the full explanation (UnitPrice is a complex property).
    public PurchaseOrderLine()
        : this(0, string.Empty, 0, default)
    {
    }

    public Money LineValue => new(OrderedQuantity * UnitPrice.Amount, UnitPrice.Currency);
}
