namespace Capstone.Invoicing.Domain;

// ApprovedBy is null exactly when Kind is DeemedBySla - nobody approved it, the SLA
// window simply elapsed. Modelled as one type with a nullable actor rather than two
// separate record shapes, because both are still "the approval record" for the same
// invoice; the Kind field is what a reader (or a court) actually needs to check.
public sealed record ApprovalRecord(DateTimeOffset ApprovedAt, ApprovalKind Kind, Guid? ApprovedBy)
{
    public static ApprovalRecord ByHuman(DateTimeOffset approvedAt, Guid approvedBy) =>
        new(approvedAt, ApprovalKind.Human, approvedBy);

    public static ApprovalRecord DeemedBySla(DateTimeOffset deemedAt) =>
        new(deemedAt, ApprovalKind.DeemedBySla, ApprovedBy: null);
}
