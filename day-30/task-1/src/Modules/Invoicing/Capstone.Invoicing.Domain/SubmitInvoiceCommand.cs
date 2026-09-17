namespace Capstone.Invoicing.Domain;

// The input to Invoice.Submit() - everything the supplier actually provides.
// Everything else Submit() needs (the PO snapshot, the terms snapshot, the matching
// policy) is assembled by the application layer and passed in alongside this,
// because an aggregate must not reach out to a repository or a port itself.
//
// CorrectsInvoiceId (Day 30) - the correction path DESIGN.md names ("a correction
// is a new invoice referencing the disputed one - never a mutation of an approved
// or disputed record") but that nothing previously carried: Reject() closes the
// old invoice out, and creating the replacement was always "a separate, later
// call to SubmitInvoiceUseCase" (see RejectInvoiceUseCase's own comment) - but
// with no way to record which invoice a new one replaces. Optional because most
// submissions are not corrections; when present, SubmitInvoiceUseCase verifies
// the referenced invoice is actually Rejected before allowing the link (see that
// use case) - Invoice.Submit itself has no repository access to check this, so
// the check belongs one layer up.
public sealed record SubmitInvoiceCommand(
    Guid SupplierId,
    string InvoiceNumber,
    string Currency,
    IReadOnlyCollection<InvoiceLineItem> Lines,
    InvoiceId? CorrectsInvoiceId = null);
