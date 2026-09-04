namespace Capstone.Invoicing.Domain;

// A real human decision and an SLA-elapsed deemed approval produce the identical
// downstream fact (terms locked, due date stands) but are NOT the same fact for
// audit purposes - a lender or a court would care which happened. Never collapse
// this distinction; see capstone/DESIGN.md, "Deemed approval".
public enum ApprovalKind
{
    Human,
    DeemedBySla
}
