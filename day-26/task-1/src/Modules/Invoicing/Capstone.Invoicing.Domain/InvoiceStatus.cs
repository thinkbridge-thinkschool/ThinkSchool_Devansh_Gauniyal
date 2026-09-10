namespace Capstone.Invoicing.Domain;

// Deliberately excludes Draft (no invariants attach to an unsubmitted invoice - it
// would force every rule below to be written "unless Draft") and Matched (matching
// is synchronous, part of Submit, and recorded as data - MatchResult - not a state
// of its own). See capstone/DESIGN.md, "State lifecycle".
public enum InvoiceStatus
{
    Submitted,
    Disputed,
    Approved,
    Rejected,
    Withdrawn
}
