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
// wires up, not a shortcut straight from a test to a DbContext. What the 25
// existing Capstone.Invoicing.Domain.Tests cannot prove - because they construct
// PurchaseOrderSnapshot/PaymentTermsSnapshot directly and never touch a
// database - is covered here instead: that a value actually written to Azure
// SQL's column/JSON shape reads back correctly, and that PO capacity
// reservation is genuinely atomic with the invoice write it accompanies.
[Collection(SqlServerCollection.Name)]
public sealed class InvoiceLifecycleTests(SqlServerFixture sql)
{
    private sealed class StubPaymentTermsLookup : IPaymentTermsLookup
    {
        public PaymentTermsSnapshot Terms { get; set; } = new(45, 10);

        public Task<PaymentTermsSnapshot> GetAgreedTermsAsync(Guid buyerId, Guid supplierId, CancellationToken cancellationToken) =>
            Task.FromResult(Terms);
    }

    private static (SubmitInvoiceUseCase Submit, ApproveInvoiceUseCase Approve, IInvoiceRepository Invoices)
        CreateUseCases(TestPersistenceScope scope, TimeProvider clock, StubPaymentTermsLookup terms)
    {
        var purchaseOrders = new PurchaseOrderEfRepository(scope.Procurement);
        var invoices = new InvoiceEfRepository(scope.Invoicing);
        var capacityPort = new ProcurementCapacityAdapter(new PurchaseOrderCapacityGateway(purchaseOrders));

        return (
            new SubmitInvoiceUseCase(invoices, capacityPort, terms, clock),
            new ApproveInvoiceUseCase(invoices, capacityPort, clock),
            invoices);
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

            var (submit, approve, _) = CreateUseCases(scope, clock, terms);
            var command = new SubmitInvoiceCommand(
                supplierId, "INV-1", "USD",
                [new InvoiceLineItem(1, 100, new Money(20m, "USD"))]);
            invoiceId = await submit.ExecuteAsync(command, new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
            await scope.SaveChangesAsync();

            clock.Now = submittedAt.AddDays(1);
            await approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
            await scope.SaveChangesAsync();
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

        var (submit, approve, _) = CreateUseCases(scope, clock, terms);
        var invoiceId = await submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-2", "USD", [new InvoiceLineItem(1, 10, new Money(50m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        await approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None));
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

        var (submit, _, _) = CreateUseCases(scope, clock, terms);

        // First invoice reserves the full 1,000 - within tolerance, succeeds.
        await submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-3A", "USD", [new InvoiceLineItem(1, 10, new Money(100m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        // A second invoice against the same PO now has nothing left to reserve -
        // this is DESIGN.md's PO-capacity race the whole point of making
        // reservation synchronous (and, for a real database, transactional) is
        // to prevent: two invoices must never both succeed past the ceiling.
        var secondCommand = new SubmitInvoiceCommand(
            supplierId, "INV-3B", "USD", [new InvoiceLineItem(1, 1, new Money(100m, "USD"))]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => submit.ExecuteAsync(
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

        var (submit, approve, invoices) = CreateUseCases(scope, clock, terms);
        var invoiceId = await submit.ExecuteAsync(
            new SubmitInvoiceCommand(supplierId, "INV-4", "USD", [new InvoiceLineItem(1, 5, new Money(40m, "USD"))]),
            new PurchaseOrderReference(poId.Value), MatchingPolicy.Default("USD"), CancellationToken.None);
        await scope.SaveChangesAsync();

        // A buyer renegotiating terms between submission and approval must never
        // retroactively move this invoice's already-locked due date - see
        // DESIGN.md's Payment Terms section and ADR 0001.
        terms.Terms = new PaymentTermsSnapshot(90, 30);
        clock.Now = submittedAt.AddDays(2);

        await approve.ExecuteAsync(invoiceId, buyerId, CancellationToken.None);
        await scope.SaveChangesAsync();

        var invoice = await invoices.FindAsync(invoiceId, CancellationToken.None);
        Assert.NotNull(invoice);
        Assert.Equal(30, invoice.Terms.NetDays);
        Assert.Equal(submittedAt.AddDays(30), invoice.DueDate);
    }
}
