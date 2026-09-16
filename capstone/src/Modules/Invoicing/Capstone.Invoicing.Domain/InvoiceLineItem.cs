using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Owned by Invoice, never referenced independently. PurchaseOrderLineNumber is what
// ties this line back to the PO line it's matched against - a billed quantity at a
// charged price, which is NOT the same concept as the PO line's ordered quantity at
// an expected price (see DESIGN.md's bounded-context language signal). The two can
// legitimately diverge; that divergence is what matching exists to check.
public sealed record InvoiceLineItem(int PurchaseOrderLineNumber, int BilledQuantity, Money UnitPrice)
{
    public Money LineAmount => new(BilledQuantity * UnitPrice.Amount, UnitPrice.Currency);
}
