using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Owned by Invoice, never referenced independently. PurchaseOrderLineNumber is what
// ties this line back to the PO line it's matched against - a billed quantity at a
// charged price, which is NOT the same concept as the PO line's ordered quantity at
// an expected price (see DESIGN.md's bounded-context language signal). The two can
// legitimately diverge; that divergence is what matching exists to check.
public sealed record InvoiceLineItem(int PurchaseOrderLineNumber, int BilledQuantity, Money UnitPrice)
{
    // EF Core materialization only - UnitPrice (Money) is mapped as a complex
    // property, and EF Core's constructor-binding materialization cannot bind a
    // constructor parameter to a complex/owned property (see Invoice.cs's private
    // parameterless constructor for the fuller explanation of the same root cause).
    public InvoiceLineItem()
        : this(0, 0, default)
    {
    }

    public Money LineAmount => new(BilledQuantity * UnitPrice.Amount, UnitPrice.Currency);
}
