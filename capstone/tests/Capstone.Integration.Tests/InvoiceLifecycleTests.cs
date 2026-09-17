using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Application.UseCases;
using Capstone.Invoicing.Domain;
using Capstone.Invoicing.Infrastructure;
using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;
using Capstone.Procurement.Infrastructure;
using Capstone.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Capstone.Integration.Tests;

// Exercises the real EF Core persistence layer (Capstone.Invoicing.Infrastructure
// and Capstone.Procurement.Infrastructure, against an actual SQL Server - see
// SqlServerFixture) through the exact same use cases and adapter Program.cs
// wires up, not a shortcut straight from a test to a DbContext. What the domain
// unit tests in Capstone.Invoicing.Domain.Tests cannot prove - because they
// construct PurchaseOrderSnapshot/PaymentTermsSnapshot directly and never touch
// a database - is covered here instead: that a value actually written to Azure
// SQL's column/JSON shape reads back correctly, that PO capacity reservation is
// genuinely atomic with the invoice write it accompanies, and (Day 30) that the
// dispute/reject/withdraw/deemed-approval paths and the InvoiceApproved outbox
// all behave the same way against a real database as they do in isolation.
[Collection(SqlServerCollection.Name)]
public sealed class InvoiceLifecycleTests(SqlServerFixture sql)
{
    private sealed class StubPaymentTermsLookup : IPaymentTermsLookup
    {
        public PaymentTermsSnapshot Terms { get; set; } = new(45, 10);

        public Task<PaymentTermsSnapshot> GetAgreedTermsAsync(Guid buyerId, Guid supplierId, CancellationToken cancellationToken) =>
            Task.FromResult(Terms);
    }

    // A spy, not a mock framework - this project has none as a dependency, and
    // one method call each is easy enough to hand-roll. Captures what
    // ApproveInvoiceUseCase/DisputeInvoiceUseCase/ApplyDeemedApprovalsUseCase
    // actually sent, without needing a real Service Bus namespace in a test.
    private sealed class FakeSupplierNotifier : ISupplierNotifier
    {
        public List<(Guid SupplierId, string InvoiceNumber, string EventType)> Notifications { get; } = [];

        public Task NotifyInvoiceApprovedAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken)
        {
            Notifications.Add((supplierId, invoiceNumber, "Approved"));
            return Task.CompletedTask;
        }

        public Task NotifyInvoiceDisputedAsync(Guid supplierId, string invoiceNumber, string reason, CancellationToken cancellationToken)
        {
            Notifications.Add((supplierId, invoiceNumber, "Disputed"));
            return Task.CompletedTask;
        }
    }

    private sealed record UseCases(
        SubmitInvoiceUseCase Submit,
        ApproveInvoiceUseCase Approve,
        DisputeInvoiceUseCase Dispute,
        RejectInvoiceUseCase Reject,
        WithdrawInvoiceUseCase Withdraw,
        ApplyDeemedApprovalsUseCase ApplyDeemedApprovals,
        IInvoiceRepository Invoices,
        FakeSupplierNotifier Notifier);

    // The REAL EfIntegrationEventOutbox is used here, not a fake - unlike the
    // notifier, the outbox's whole point is a transactional guarantee against
    // the real database, which is exactly what this test class exists to
    // prove; faking it would prove nothing.
    private static UseCases CreateUseCases(TestPersistenceScope scope, TimeProvider clock, StubPaymentTermsLookup terms)
    {
        var purchaseOrders = new PurchaseOrderEfRepository(scope.Procurement);
        var invoices = new InvoiceEfRepository(scope.Invoicing);
        var capacityPort = new ProcurementCapacityAdapter(new PurchaseOrderCapacityGateway(purchaseOrders));
        var outbox = new EfIntegrationEventOutbox(scope.Invoicing);
        var notifier = new FakeSupplierNotifier();

        return new UseCases(
            new SubmitInvoiceUseCase(invoices, capacityPort, terms, clock),
            new ApproveInvoiceUseCase(invoices, capacityPort, outbox, notifier, clock),
            new DisputeInvoiceUseCase(invoices, notifier, clock),
            new RejectInvoiceUseCase(invoices, capacityPort, clock),
            new WithdrawInvoiceUseCase(invoices, capacityPort, clock),
            new ApplyDeemedApprovalsUseCase(invoices, capacityPort, outbox, notifier, clock),
            invoices,
            notifier);
    }

    [Fact]
    public async Task SubmitThenApprove_PersistsToRealDatabase_WithDueDateLockedAtSubmission()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(submittedAt);
        var terms = new StubPaymentTermsLookup { Terms = new PaymentTermsSnapshot(45, 10) };

        InvoiceId invoiceId;
        await using (var scope = new TestPersistenceScope(sql.ConnectionString))
        {
            var poId = PurchaseOrderId.New();
            var purchaseOrder = PurchaseOrder.Issue(
                poId, supplierId, buyerId, "USD",
                [new PurchaseOrderLine(1, "Widgets", 100, new Money(20m, "USD"))]);
            await scope.Procurement.PurchaseOrders.AddAsync(purchaseOrder);
            await scope.SaveChangesAsync();

            var useCases = CreateUseCases(scope, clock, terms);
            var command = new SubmitInvoiceCommand(
                supplierId, "INV-1", "USD",
                [new InvoiceLineItem(1, 100, new Money(20m, "USD"))]);
            invoiceId = await useCases.Submit.ExecuteAsync(command, new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
            await scope.SaveChangesAsync();

            clock.Now = submittedAt.AddDays(1);
            await useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
            await scope.SaveChangesAsync();

            Assert.Single(useCases.Notifier.Notifications, n => n.EventType == "Approved" && n.InvoiceNumber == "INV-1");
        }

        // A brand-new scope, brand-new DbContexts, brand-new SqlConnection - if
        // this still finds the invoice Approved with the right due date, the
        // data really left the process and came back from SQL Server, not from
        // an EF change-tracker cache still warm from the calls above.
        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var reloaded = await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);

        Assert.NotNull(reloaded);
        Assert.Equal(InvoiceStatus.Approved, reloaded.Status);
        Assert.Equal(ApprovalKind.Human, reloaded.Approval!.Kind);
        Assert.Equal(buyerId, reloaded.Approval.ApprovedBy);
        // The central rule (DESIGN.md): due date = submission + term days,
        // computed at submission - unaffected by approval happening a day later.
        Assert.Equal(submittedAt.AddDays(45), reloaded.DueDate);
        Assert.Equal(45, reloaded.Terms.NetDays);
        Assert.Single(reloaded.Lines);
        Assert.Equal(2000m, reloaded.Total.Amount);
    }

    [Fact]
    public async Task Approve_WritesAnOutboxRow_InTheSameTransactionAsTheInvoiceState()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-OUTBOX", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        // Fresh scope - proves the row is really in SQL, not just tracked in
        // this process's change tracker.
        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var outboxMessage = await freshScope.Invoicing.OutboxMessages.SingleAsync(m => m.EventType == "InvoiceApproved" && m.Payload.Contains(invoiceId.Value.ToString()));
        Assert.Null(outboxMessage.PublishedAt); // Nobody has relayed it yet - that's OutboxRelayBackgroundService's job, not this use case's.
        Assert.Contains("Human", outboxMessage.Payload);
    }

    [Fact]
    public async Task Approve_ASecondTime_IsRejected()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-2", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None));
        Assert.Contains("Approved", ex.Message);
    }

    [Fact]
    public async Task Submit_ExceedingRemainingPurchaseOrderCapacity_IsRejected_AndReservesNothing()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        // Total capacity 1,000 USD (10 x 100).
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(100m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);

        // First invoice reserves the full 1,000 - within tolerance, succeeds.
        await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-3A", "USD", [new InvoiceLineItem(1, 10, new Money(100m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        // A second invoice against the same PO now has nothing left to reserve -
        // this is DESIGN.md's PO-capacity race the whole point of making
        // reservation synchronous (and, for a real database, transactional) is
        // to prevent: two invoices must never both succeed past the ceiling.
        var secondCommand = new SubmitInvoiceCommand(
            supplierId, "INV-3B", "USD", [new InvoiceLineItem(1, 1, new Money(100m, "USD"))]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => useCases.Submit.ExecuteAsync(
            secondCommand, new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None));
        Assert.Contains("exceeds", ex.Message);

        // The rejected second invoice must not have reserved anything, and must
        // not have been persisted at all - confirmed against the database, not
        // the in-memory use case result. Checked by invoice number rather than
        // "the whole table has exactly one row", since the fixture's database
        // is shared across every test in this class.
        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        Assert.NotNull(await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.InvoiceNumber == "INV-3A", CancellationToken.None));
        Assert.Null(await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.InvoiceNumber == "INV-3B", CancellationToken.None));

        var purchaseOrder = await freshScope.Procurement.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId, CancellationToken.None);
        Assert.NotNull(purchaseOrder);
        Assert.Equal(0m, purchaseOrder.Available.Amount);
        Assert.Equal(1000m, purchaseOrder.Reserved.Amount);
    }

    [Fact]
    public async Task ApprovalNeverRereadsPaymentTerms_TheSnapshotCapturedAtSubmissionStands()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(submittedAt);
        var terms = new StubPaymentTermsLookup { Terms = new PaymentTermsSnapshot(30, 5) };

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 5, new Money(40m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-4", "USD", [new InvoiceLineItem(1, 5, new Money(40m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        // A buyer renegotiating terms between submission and approval must never
        // retroactively move this invoice's already-locked due date - see
        // DESIGN.md's Payment Terms section and ADR 0001.
        terms.Terms = new PaymentTermsSnapshot(90, 30);
        clock.Now = submittedAt.AddDays(2);

        await useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        var invoice = await useCases.Invoices.FindAsync(invoiceId, CancellationToken.None);
        Assert.NotNull(invoice);
        Assert.Equal(30, invoice.Terms.NetDays);
        Assert.Equal(submittedAt.AddDays(30), invoice.DueDate);
    }

    // ===== Dispute -> Approve (resolved in the supplier's favour) =====

    [Fact]
    public async Task Dispute_ThenApprove_ResolvesInSuppliersFavour_ConsumesReservation_NotifiesTwice()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-DISPUTE-APPROVE", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Dispute.ExecuteAsync(invoiceId, "Goods arrived a week late.", CancellationToken.None);
        await scope.SaveChangesAsync();

        var disputed = await useCases.Invoices.FindAsync(invoiceId, CancellationToken.None);
        Assert.Equal(InvoiceStatus.Disputed, disputed!.Status);

        // Resolved in the supplier's favour - Approve() accepts a Disputed
        // invoice unchanged (see Invoice.EnsureCanBeApproved).
        await useCases.Approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var reloaded = await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Approved, reloaded!.Status);

        var purchaseOrder = await freshScope.Procurement.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        // The reservation became consumption - not released - since the
        // dispute was resolved by accepting the invoice, not rejecting it.
        Assert.Equal(0m, purchaseOrder!.Reserved.Amount);
        Assert.Equal(500m, purchaseOrder.Consumed.Amount);

        Assert.Equal(2, useCases.Notifier.Notifications.Count);
        Assert.Contains(useCases.Notifier.Notifications, n => n.EventType == "Disputed");
        Assert.Contains(useCases.Notifier.Notifications, n => n.EventType == "Approved");
    }

    // ===== Dispute -> Reject -> corrective resubmission =====

    [Fact]
    public async Task Dispute_ThenReject_ReleasesCapacity_AndAllowsACorrectiveResubmission()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var originalInvoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-REJECT-ORIGINAL", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Dispute.ExecuteAsync(originalInvoiceId, "Wrong item entirely.", CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Reject.ExecuteAsync(originalInvoiceId, "Supplier must submit a corrected invoice.", CancellationToken.None);
        await scope.SaveChangesAsync();

        var poAfterReject = await scope.Procurement.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        Assert.Equal(500m, poAfterReject!.Available.Amount); // The full reservation was released.

        // The corrective resubmission - same PO, references the rejected invoice.
        var correctiveInvoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(
                supplierId, "INV-REJECT-CORRECTIVE", "USD",
                [new InvoiceLineItem(1, 10, new Money(50m, "USD"))],
                originalInvoiceId),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var corrective = await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.Id == correctiveInvoiceId);
        Assert.Equal(originalInvoiceId, corrective!.CorrectsInvoiceId);
    }

    [Fact]
    public async Task Submit_CorrectingAnInvoiceThatIsNotRejected_Throws()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 20, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var stillSubmittedInvoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-NOT-REJECTED", "USD", [new InvoiceLineItem(1, 5, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(
                supplierId, "INV-BOGUS-CORRECTION", "USD",
                [new InvoiceLineItem(1, 5, new Money(50m, "USD"))],
                stillSubmittedInvoiceId),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None));
        Assert.Contains("not Rejected", ex.Message);
    }

    // ===== Withdraw =====

    [Fact]
    public async Task Withdraw_FromSubmitted_ReleasesCapacity_PersistedAcrossAFreshConnection()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var terms = new StubPaymentTermsLookup();

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-WITHDRAW", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await useCases.Withdraw.ExecuteAsync(invoiceId, CancellationToken.None);
        await scope.SaveChangesAsync();

        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var reloaded = await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Withdrawn, reloaded!.Status);

        var purchaseOrder = await freshScope.Procurement.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        Assert.Equal(500m, purchaseOrder!.Available.Amount);
        Assert.Equal(0m, purchaseOrder.Reserved.Amount);
    }

    // ===== Deemed approval: firing, not firing, and never overriding a dispute =====

    [Fact]
    public async Task ApplyDeemedApprovals_FiresAfterReviewWindowElapses_WritesOutboxRow_AndNotifiesSupplier()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(submittedAt);
        var terms = new StubPaymentTermsLookup { Terms = new PaymentTermsSnapshot(45, reviewWindowDays: 2) };

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-DEEMED-FIRES", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        clock.Now = submittedAt.AddDays(3); // Past the 2-day review window.
        var deemed = await useCases.ApplyDeemedApprovals.ExecuteAsync(CancellationToken.None);
        await scope.SaveChangesAsync();

        Assert.Contains(invoiceId, deemed);

        await using var freshScope = new TestPersistenceScope(sql.ConnectionString);
        var reloaded = await freshScope.Invoicing.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Approved, reloaded!.Status);
        Assert.Equal(ApprovalKind.DeemedBySla, reloaded.Approval!.Kind);
        Assert.Null(reloaded.Approval.ApprovedBy);

        var purchaseOrder = await freshScope.Procurement.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        Assert.Equal(500m, purchaseOrder!.Consumed.Amount);

        var outboxMessage = await freshScope.Invoicing.OutboxMessages.SingleAsync(m => m.Payload.Contains(invoiceId.Value.ToString()));
        Assert.Contains("DeemedBySla", outboxMessage.Payload);

        Assert.Single(useCases.Notifier.Notifications, n => n.EventType == "Approved" && n.InvoiceNumber == "INV-DEEMED-FIRES");
    }

    [Fact]
    public async Task ApplyDeemedApprovals_DoesNotFireBeforeReviewWindowElapses()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(submittedAt);
        var terms = new StubPaymentTermsLookup { Terms = new PaymentTermsSnapshot(45, reviewWindowDays: 10) };

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-DEEMED-NOT-YET", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        clock.Now = submittedAt.AddDays(5); // Still inside the 10-day window.
        var deemed = await useCases.ApplyDeemedApprovals.ExecuteAsync(CancellationToken.None);
        await scope.SaveChangesAsync();

        Assert.DoesNotContain(invoiceId, deemed);

        var invoice = await useCases.Invoices.FindAsync(invoiceId, CancellationToken.None);
        Assert.Equal(InvoiceStatus.Submitted, invoice!.Status);
        Assert.Empty(useCases.Notifier.Notifications);
    }

    [Fact]
    public async Task ApplyDeemedApprovals_NeverOverridesAnActiveDispute()
    {
        var supplierId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(submittedAt);
        var terms = new StubPaymentTermsLookup { Terms = new PaymentTermsSnapshot(45, reviewWindowDays: 2) };

        await using var scope = new TestPersistenceScope(sql.ConnectionString);
        var poId = PurchaseOrderId.New();
        await scope.Procurement.PurchaseOrders.AddAsync(PurchaseOrder.Issue(
            poId, supplierId, buyerId, "USD", [new PurchaseOrderLine(1, "Widgets", 10, new Money(50m, "USD"))]));
        await scope.SaveChangesAsync();

        var useCases = CreateUseCases(scope, clock, terms);
        var invoiceId = await useCases.Submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-DEEMED-VS-DISPUTE", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        // The buyer disputes BEFORE the window elapses - the sweep must never
        // treat this as "no action taken" once it later runs past the deadline.
        await useCases.Dispute.ExecuteAsync(invoiceId, "Quantity mismatch.", CancellationToken.None);
        await scope.SaveChangesAsync();

        clock.Now = submittedAt.AddDays(3); // Past the review window.
        var deemed = await useCases.ApplyDeemedApprovals.ExecuteAsync(CancellationToken.None);
        await scope.SaveChangesAsync();

        Assert.Empty(deemed);

        var invoice = await useCases.Invoices.FindAsync(invoiceId, CancellationToken.None);
        Assert.Equal(InvoiceStatus.Disputed, invoice!.Status); // Still disputed, not silently deemed-approved.
    }
}
