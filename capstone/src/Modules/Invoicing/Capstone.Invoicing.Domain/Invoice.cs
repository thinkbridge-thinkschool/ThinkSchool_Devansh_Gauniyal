using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// The aggregate this whole slice exists to build. See capstone/DESIGN.md for the
// invariants in prose; this is where they're enforced. Two kinds of failure are
// deliberately different here (see DESIGN.md, matching tolerance):
//   - structural/authorisation failures (wrong vendor, closed PO, insufficient PO
//     capacity, an unknown PO line) throw from Submit() - no Invoice is created.
//   - a line priced outside the configured matching tolerance does NOT throw - the
//     Invoice is created in Disputed status, because a pricing disagreement is
//     something to resolve, not a malformed request to reject.
public sealed class Invoice : AggregateRoot<InvoiceId>
{
    private readonly List<InvoiceLineItem> _lines;

    private Invoice(
        InvoiceId id,
        Guid supplierId,
        Guid buyerId,
        PurchaseOrderReference purchaseOrderId,
        string invoiceNumber,
        string currency,
        IReadOnlyCollection<InvoiceLineItem> lines,
        PaymentTermsSnapshot terms,
        MatchResult matchResult,
        DateTimeOffset submittedAt)
        : base(id)
    {
        SupplierId = supplierId;
        BuyerId = buyerId;
        PurchaseOrderId = purchaseOrderId;
        InvoiceNumber = invoiceNumber;
        Currency = currency;
        _lines = [.. lines];
        Terms = terms;
        MatchResult = matchResult;
        SubmittedAt = submittedAt;

        // The central rule (see DESIGN.md): the due date is knowable the instant the
        // invoice is submitted, because it depends only on the one timestamp the
        // buyer cannot manipulate - never on when (or whether) the buyer acts.
        DueDate = submittedAt.AddDays(terms.NetDays);

        Status = matchResult.WithinTolerance ? InvoiceStatus.Submitted : InvoiceStatus.Disputed;
        DisputeReason = matchResult.WithinTolerance
            ? null
            : "Line amount(s) outside the configured matching tolerance - raised automatically at submission.";
    }

    public Guid SupplierId { get; }
    public Guid BuyerId { get; }
    public PurchaseOrderReference PurchaseOrderId { get; }
    public string InvoiceNumber { get; }
    public string Currency { get; }
    public IReadOnlyCollection<InvoiceLineItem> Lines => _lines;
    public PaymentTermsSnapshot Terms { get; }
    public MatchResult MatchResult { get; }
    public DateTimeOffset SubmittedAt { get; }

    // Stored, not recomputed on every read - see DESIGN.md: a later change to how
    // terms are calculated must never silently move a date already communicated to
    // the supplier.
    public DateTimeOffset DueDate { get; }

    public InvoiceStatus Status { get; private set; }
    public string? DisputeReason { get; private set; }
    public ApprovalRecord? Approval { get; private set; }

    public Money Total => _lines
        .Select(l => l.LineAmount)
        .Aggregate(Money.Zero(Currency), (sum, line) => sum + line);

    public static Invoice Submit(
        SubmitInvoiceCommand command,
        PurchaseOrderSnapshot purchaseOrder,
        PaymentTermsSnapshot terms,
        MatchingPolicy matchingPolicy,
        TimeProvider clock)
    {
        if (command.Lines.Count == 0)
        {
            throw new InvalidOperationException("An invoice must have at least one line.");
        }

        if (purchaseOrder.VendorId != command.SupplierId)
        {
            throw new InvalidOperationException(
                $"Supplier {command.SupplierId} is not the vendor on purchase order {purchaseOrder.Id}.");
        }

        if (!purchaseOrder.IsOpen)
        {
            throw new InvalidOperationException($"Purchase order {purchaseOrder.Id} is not open.");
        }

        if (command.Currency != purchaseOrder.Currency)
        {
            throw new InvalidOperationException(
                $"Invoice currency {command.Currency} does not match purchase order currency {purchaseOrder.Currency}.");
        }

        var poLinesByNumber = purchaseOrder.Lines.ToDictionary(l => l.LineNumber);
        var variances = new List<LineVariance>();
        var total = Money.Zero(command.Currency);

        foreach (var line in command.Lines)
        {
            if (!poLinesByNumber.TryGetValue(line.PurchaseOrderLineNumber, out var poLine))
            {
                throw new InvalidOperationException(
                    $"Purchase order {purchaseOrder.Id} has no line {line.PurchaseOrderLineNumber}.");
            }

            total += line.LineAmount;

            var withinTolerance = matchingPolicy.IsWithinTolerance(line.LineAmount, poLine.LineValue);
            var difference = line.LineAmount > poLine.LineValue
                ? line.LineAmount - poLine.LineValue
                : poLine.LineValue - line.LineAmount;

            if (!withinTolerance)
            {
                variances.Add(new LineVariance(line.PurchaseOrderLineNumber, line.LineAmount, poLine.LineValue, difference));
            }
        }

        // Capacity is checked against the FULL invoiced total regardless of match
        // outcome - a line priced outside tolerance still reserves its amount (see
        // DESIGN.md: in-flight, including Disputed, invoices reserve capacity). This
        // is a hard failure, never a dispute: there is no capacity to even hold a
        // reservation against, so there is nothing to resolve a disagreement about.
        if (total > purchaseOrder.Available)
        {
            throw new InvalidOperationException(
                $"Invoice total {total} exceeds the {purchaseOrder.Available} available on purchase order {purchaseOrder.Id}.");
        }

        var matchResult = new MatchResult(variances.Count == 0, variances);
        var submittedAt = clock.GetUtcNow();

        var invoice = new Invoice(
            InvoiceId.New(),
            command.SupplierId,
            purchaseOrder.BuyerId,
            purchaseOrder.Id,
            command.InvoiceNumber,
            command.Currency,
            command.Lines,
            terms,
            matchResult,
            submittedAt);

        invoice.Raise(new InvoiceSubmitted(invoice.Id, total, submittedAt));
        return invoice;
    }

    // Covers both the happy path (Submitted -> Approved) and a dispute resolved in
    // the supplier's favour (Disputed -> Approved, invoice unchanged). A dispute that
    // requires a CHANGE to the invoice is never resolved by mutating this instance -
    // see Reject().
    public void Approve(Guid approvingActorId, TimeProvider clock)
    {
        EnsureCanBeApproved();
        var record = ApprovalRecord.ByHuman(clock.GetUtcNow(), approvingActorId);
        ApplyApproval(record);
    }

    // System-triggered, not called by a human actor - see the application layer's
    // scheduled use case. Only a Submitted invoice the buyer never acted on is
    // eligible; an active Dispute means the buyer DID act, just not by approving, so
    // it is deliberately excluded here.
    public void ApplyDeemedApproval(TimeProvider clock)
    {
        if (Status != InvoiceStatus.Submitted)
        {
            throw new InvalidOperationException(
                $"Invoice {Id} is {Status}, not Submitted - deemed approval only applies to an untouched Submitted invoice.");
        }

        var deadline = SubmittedAt.AddDays(Terms.ReviewWindowDays);
        if (clock.GetUtcNow() < deadline)
        {
            throw new InvalidOperationException(
                $"Invoice {Id}'s review window does not elapse until {deadline:O}.");
        }

        ApplyApproval(ApprovalRecord.DeemedBySla(deadline));
    }

    public void Dispute(string reason, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A dispute requires a reason.", nameof(reason));
        }

        if (Status != InvoiceStatus.Submitted)
        {
            throw new InvalidOperationException($"Invoice {Id} is {Status}, not Submitted - cannot dispute.");
        }

        Status = InvoiceStatus.Disputed;
        DisputeReason = reason;
        Raise(new InvoiceDisputed(Id, reason, clock.GetUtcNow()));
    }

    // The correction path: the dispute stands, so this invoice is done, and whoever
    // orchestrates this (the application layer) is responsible for creating a NEW
    // invoice that references this one. Never mutate this invoice's lines/amount -
    // it's already been shown to the buyer once, disputed, and must keep saying what
    // it said.
    public void Reject(string reason, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection requires a reason.", nameof(reason));
        }

        if (Status != InvoiceStatus.Disputed)
        {
            throw new InvalidOperationException($"Invoice {Id} is {Status}, not Disputed - cannot reject.");
        }

        Status = InvoiceStatus.Rejected;
        Raise(new InvoiceRejected(Id, reason, Total, clock.GetUtcNow()));
    }

    public void Withdraw(TimeProvider clock)
    {
        if (Status != InvoiceStatus.Submitted)
        {
            throw new InvalidOperationException($"Invoice {Id} is {Status}, not Submitted - cannot withdraw.");
        }

        Status = InvoiceStatus.Withdrawn;
        Raise(new InvoiceWithdrawn(Id, Total, clock.GetUtcNow()));
    }

    private void EnsureCanBeApproved()
    {
        if (Status is not (InvoiceStatus.Submitted or InvoiceStatus.Disputed))
        {
            throw new InvalidOperationException(
                $"Invoice {Id} is {Status} - only a Submitted or Disputed invoice can be approved.");
        }
    }

    private void ApplyApproval(ApprovalRecord record)
    {
        // Once set, nothing below this point can run again - Status is no longer
        // Submitted or Disputed, so every other mutator's guard rejects the call.
        // This is the "once approved, immutable" invariant, enforced structurally
        // rather than by a separate check.
        Status = InvoiceStatus.Approved;
        Approval = record;
        Raise(new InvoiceApproved(Id, record, Total, record.ApprovedAt));
    }
}
