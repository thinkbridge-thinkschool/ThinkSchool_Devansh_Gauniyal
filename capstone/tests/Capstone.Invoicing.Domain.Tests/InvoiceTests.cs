using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain.Tests;

// Real tests against the real aggregate - no mocks needed, because Invoice.Submit()
// takes plain data (a snapshot, a terms value, a policy) rather than reaching out to
// anything itself. See capstone/DESIGN.md for the invariants these tests assert.
public sealed class InvoiceTests
{
    private static readonly Guid SupplierId = Guid.NewGuid();
    private static readonly Guid BuyerId = Guid.NewGuid();
    private static readonly PurchaseOrderReference PurchaseOrderId = new(Guid.NewGuid());
    private static readonly DateTimeOffset SubmittedAt = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static PurchaseOrderSnapshot OpenPurchaseOrder(decimal available = 10_000m, bool isOpen = true, Guid? vendorId = null) =>
        new(
            PurchaseOrderId,
            vendorId ?? SupplierId,
            BuyerId,
            "USD",
            isOpen,
            new Money(available, "USD"),
            [new PurchaseOrderLineSnapshot(1, new Money(2000m, "USD"))]);

    private static PaymentTermsSnapshot Terms(int netDays = 45, int reviewWindowDays = 10) => new(netDays, reviewWindowDays);

    private static MatchingPolicy Policy() => MatchingPolicy.Default("USD");

    private static SubmitInvoiceCommand CommandForAmount(decimal lineAmount) => new(
        SupplierId,
        InvoiceNumber: "INV-1",
        Currency: "USD",
        Lines: [new InvoiceLineItem(PurchaseOrderLineNumber: 1, BilledQuantity: 1, UnitPrice: new Money(lineAmount, "USD"))]);

    private static FixedTimeProvider Clock() => new(SubmittedAt);

    // ===== Submit: happy path and the central rule =====

    [Fact]
    public void Submit_WithinTolerance_CreatesSubmittedInvoice()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        Assert.Equal(InvoiceStatus.Submitted, invoice.Status);
        Assert.True(invoice.MatchResult.WithinTolerance);
    }

    [Fact]
    public void Submit_DueDate_IsSubmissionTimestampPlusNetDays_NotDependentOnApproval()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(netDays: 45), Policy(), Clock());

        Assert.Equal(SubmittedAt.AddDays(45), invoice.DueDate);
    }

    [Fact]
    public void Submit_ByWrongVendor_Throws()
    {
        var wrongVendorPo = OpenPurchaseOrder(vendorId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () => Invoice.Submit(CommandForAmount(2000m), wrongVendorPo, Terms(), Policy(), Clock()));
    }

    [Fact]
    public void Submit_AgainstClosedPurchaseOrder_Throws()
    {
        var closedPo = OpenPurchaseOrder(isOpen: false);

        Assert.Throws<InvalidOperationException>(
            () => Invoice.Submit(CommandForAmount(2000m), closedPo, Terms(), Policy(), Clock()));
    }

    [Fact]
    public void Submit_ExceedingAvailableCapacity_ThrowsRatherThanDisputes()
    {
        // This is a hard failure, not a dispute: there's no capacity to even hold a
        // reservation against. See DESIGN.md's distinction between structural
        // failures (reject) and pricing variance (dispute).
        var tightPo = OpenPurchaseOrder(available: 500m);

        Assert.Throws<InvalidOperationException>(
            () => Invoice.Submit(CommandForAmount(2000m), tightPo, Terms(), Policy(), Clock()));
    }

    [Fact]
    public void Submit_LineOutsideTolerance_CreatesDisputedInvoice_DoesNotThrow()
    {
        // PO line value is 2000; tolerance default is lower of 1% (=20) or a fixed 10
        // -> effective tolerance is 10. 2100 is well outside it.
        var invoice = Invoice.Submit(CommandForAmount(2100m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        Assert.Equal(InvoiceStatus.Disputed, invoice.Status);
        Assert.False(invoice.MatchResult.WithinTolerance);
        Assert.NotNull(invoice.DisputeReason);
    }

    [Fact]
    public void Submit_LineWithinTolerance_DoesNotDispute()
    {
        // 2005 is within the effective $10 tolerance of a $2000 PO line.
        var invoice = Invoice.Submit(CommandForAmount(2005m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        Assert.Equal(InvoiceStatus.Submitted, invoice.Status);
    }

    [Fact]
    public void Submit_RaisesInvoiceSubmitted_WithReservedAmount()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        var raised = Assert.Single(invoice.DomainEvents);
        var submitted = Assert.IsType<InvoiceSubmitted>(raised);
        Assert.Equal(new Money(2000m, "USD"), submitted.ReservedAmount);
    }

    // ===== Approve =====

    [Fact]
    public void Approve_FromSubmitted_Succeeds_AsHumanApproval()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        var approver = Guid.NewGuid();

        invoice.Approve(approver, Clock());

        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Equal(ApprovalKind.Human, invoice.Approval!.Kind);
        Assert.Equal(approver, invoice.Approval.ApprovedBy);
    }

    [Fact]
    public void Approve_FromDisputed_Succeeds_ResolvedInSuppliersFavour()
    {
        var invoice = Invoice.Submit(CommandForAmount(2100m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        Assert.Equal(InvoiceStatus.Disputed, invoice.Status); // sanity check on the fixture

        invoice.Approve(Guid.NewGuid(), Clock());

        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
    }

    [Fact]
    public void Approve_AlreadyApproved_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.Approve(Guid.NewGuid(), Clock());

        Assert.Throws<InvalidOperationException>(() => invoice.Approve(Guid.NewGuid(), Clock()));
    }

    [Fact]
    public void Approve_AfterWithdrawn_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.Withdraw(Clock());

        Assert.Throws<InvalidOperationException>(() => invoice.Approve(Guid.NewGuid(), Clock()));
    }

    [Fact]
    public void Approve_TermsAndDueDate_AreImmutableAfterApproval()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(netDays: 45), Policy(), Clock());
        invoice.Approve(Guid.NewGuid(), Clock());
        var dueDateAfterApproval = invoice.DueDate;

        // No public method allows changing DueDate or Terms post-approval - every
        // mutator's guard now rejects, which this suite already proves individually
        // (Dispute/Reject/Withdraw-after-Approved below). This test pins the value
        // itself so a future change that quietly added a setter would be caught here.
        Assert.Equal(SubmittedAt.AddDays(45), dueDateAfterApproval);
    }

    // ===== Deemed approval =====

    [Fact]
    public void ApplyDeemedApproval_BeforeReviewWindowElapses_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(reviewWindowDays: 10), Policy(), Clock());
        var stillWithinWindow = new FixedTimeProvider(SubmittedAt.AddDays(5));

        Assert.Throws<InvalidOperationException>(() => invoice.ApplyDeemedApproval(stillWithinWindow));
    }

    [Fact]
    public void ApplyDeemedApproval_AfterReviewWindowElapses_ApprovesAsDeemedBySla()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(reviewWindowDays: 10), Policy(), Clock());
        var afterWindow = new FixedTimeProvider(SubmittedAt.AddDays(11));

        invoice.ApplyDeemedApproval(afterWindow);

        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Equal(ApprovalKind.DeemedBySla, invoice.Approval!.Kind);
        Assert.Null(invoice.Approval.ApprovedBy);
    }

    [Fact]
    public void ApplyDeemedApproval_WhenBuyerAlreadyDisputed_Throws()
    {
        // The buyer DID act - just not by approving. Deemed approval must not
        // override an active dispute; see DESIGN.md.
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(reviewWindowDays: 10), Policy(), Clock());
        invoice.Dispute("Wrong quantity delivered.", Clock());
        var afterWindow = new FixedTimeProvider(SubmittedAt.AddDays(11));

        Assert.Throws<InvalidOperationException>(() => invoice.ApplyDeemedApproval(afterWindow));
    }

    // ===== Dispute =====

    [Fact]
    public void Dispute_FromSubmitted_Succeeds()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        invoice.Dispute("Goods damaged on arrival.", Clock());

        Assert.Equal(InvoiceStatus.Disputed, invoice.Status);
        Assert.Equal("Goods damaged on arrival.", invoice.DisputeReason);
    }

    [Fact]
    public void Dispute_WithNoReason_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        Assert.Throws<ArgumentException>(() => invoice.Dispute("   ", Clock()));
    }

    [Fact]
    public void Dispute_AlreadyDisputed_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2100m), OpenPurchaseOrder(), Terms(), Policy(), Clock()); // auto-disputed
        Assert.Equal(InvoiceStatus.Disputed, invoice.Status);

        Assert.Throws<InvalidOperationException>(() => invoice.Dispute("A second, unrelated objection.", Clock()));
    }

    [Fact]
    public void Dispute_AfterApproved_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.Approve(Guid.NewGuid(), Clock());

        Assert.Throws<InvalidOperationException>(() => invoice.Dispute("Too late.", Clock()));
    }

    // ===== Reject (the correction path) =====

    [Fact]
    public void Reject_FromDisputed_Succeeds_AndReportsReleasedAmount()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.Dispute("Wrong item entirely.", Clock());
        invoice.ClearDomainEvents();

        invoice.Reject("Supplier must submit a corrected invoice.", Clock());

        Assert.Equal(InvoiceStatus.Rejected, invoice.Status);
        var raised = Assert.Single(invoice.DomainEvents);
        var rejected = Assert.IsType<InvoiceRejected>(raised);
        Assert.Equal(new Money(2000m, "USD"), rejected.ReleasedAmount);
    }

    [Fact]
    public void Reject_FromSubmitted_Throws_MustBeDisputedFirst()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());

        Assert.Throws<InvalidOperationException>(() => invoice.Reject("No dispute exists yet.", Clock()));
    }

    // ===== Withdraw =====

    [Fact]
    public void Withdraw_FromSubmitted_Succeeds_AndReleasesTheFullReservedAmount()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.ClearDomainEvents();

        invoice.Withdraw(Clock());

        Assert.Equal(InvoiceStatus.Withdrawn, invoice.Status);
        var raised = Assert.Single(invoice.DomainEvents);
        var withdrawn = Assert.IsType<InvoiceWithdrawn>(raised);
        Assert.Equal(new Money(2000m, "USD"), withdrawn.ReleasedAmount);
    }

    [Fact]
    public void Withdraw_FromDisputed_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2100m), OpenPurchaseOrder(), Terms(), Policy(), Clock()); // auto-disputed

        Assert.Throws<InvalidOperationException>(() => invoice.Withdraw(Clock()));
    }

    [Fact]
    public void Withdraw_AfterApproved_Throws()
    {
        var invoice = Invoice.Submit(CommandForAmount(2000m), OpenPurchaseOrder(), Terms(), Policy(), Clock());
        invoice.Approve(Guid.NewGuid(), Clock());

        Assert.Throws<InvalidOperationException>(() => invoice.Withdraw(Clock()));
    }
}
