namespace Capstone.Invoicing.Domain;

// The input to Invoice.Submit() - everything the supplier actually provides.
// Everything else Submit() needs (the PO snapshot, the terms snapshot, the matching
// policy) is assembled by the application layer and passed in alongside this,
// because an aggregate must not reach out to a repository or a port itself.
public sealed record SubmitInvoiceCommand(
    Guid SupplierId,
    string InvoiceNumber,
    string Currency,
    IReadOnlyCollection<InvoiceLineItem> Lines);
