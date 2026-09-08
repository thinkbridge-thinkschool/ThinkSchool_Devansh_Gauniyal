namespace Capstone.Invoicing.Domain;

// Captured ON the invoice at submission - never a live lookup at approval time. If
// terms were read live, a buyer renegotiating Net 45 down to Net 30 (or up to Net 90)
// could retroactively move the due date of an invoice already in flight. This
// snapshot, plus SubmittedAt, is the entire due-date derivation - see Invoice.Submit().
// ReviewWindowDays is the deemed-approval SLA (see DESIGN.md); it travels with the
// terms snapshot because it's part of the same pre-agreed relationship, not a
// system-wide constant.
public sealed record PaymentTermsSnapshot
{
    public int NetDays { get; }
    public int ReviewWindowDays { get; }

    public PaymentTermsSnapshot(int netDays, int reviewWindowDays)
    {
        if (netDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(netDays), "Net payment days must be positive.");
        }

        if (reviewWindowDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewWindowDays), "Review window must be positive.");
        }

        NetDays = netDays;
        ReviewWindowDays = reviewWindowDays;
    }
}
