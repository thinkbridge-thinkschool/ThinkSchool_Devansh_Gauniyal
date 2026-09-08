using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Application.UseCases;
using Capstone.Invoicing.Domain;
using Capstone.Invoicing.Infrastructure;
using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;
using Capstone.Procurement.Infrastructure;
using Capstone.SharedKernel;

// Deliberately thin: this host wires dependency injection and exposes just enough
// to prove the module composition resolves and runs, nothing more. No persistence,
// no auth, no UI - see capstone/README.md, "what's deliberately not built yet". The
// two POST/GET endpoints below exist to demonstrate the wiring end to end (the
// dependency graph really does resolve, the modules really do talk to each other
// only through the port/adapter shown in ProcurementCapacityAdapter.cs); they are
// not the deliverable.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

// Procurement module.
builder.Services.AddSingleton<IPurchaseOrderRepository, InMemoryPurchaseOrderRepository>();
builder.Services.AddSingleton<IPurchaseOrderCapacityGateway, PurchaseOrderCapacityGateway>();

// Invoicing module. Its only knowledge of Procurement is through the port it
// defines itself (IPurchaseOrderCapacityPort) and the adapter that implements it.
builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
builder.Services.AddSingleton<IPaymentTermsLookup, InMemoryPaymentTermsLookup>();
builder.Services.AddSingleton<IPurchaseOrderCapacityPort, ProcurementCapacityAdapter>();

builder.Services.AddScoped<SubmitInvoiceUseCase>();
builder.Services.AddScoped<ApproveInvoiceUseCase>();
builder.Services.AddScoped<DisputeInvoiceUseCase>();
builder.Services.AddScoped<RejectInvoiceUseCase>();
builder.Services.AddScoped<WithdrawInvoiceUseCase>();
builder.Services.AddScoped<ApplyDeemedApprovalsUseCase>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { message = "Capstone host is running." }));

// Seeds a single in-memory purchase order and submits one invoice against it, so a
// visitor can see the full Submit -> reserve-capacity round trip actually execute
// through both modules, without a database. Demo scaffolding, not a real endpoint
// shape - see README.md.
app.MapPost("/demo/submit-sample-invoice", async (
    IPurchaseOrderRepository purchaseOrders,
    SubmitInvoiceUseCase submitInvoice,
    CancellationToken cancellationToken) =>
{
    var supplierId = Guid.NewGuid();
    var buyerId = Guid.NewGuid();
    var poId = PurchaseOrderId.New();

    var purchaseOrder = PurchaseOrder.Issue(
        poId, supplierId, buyerId, "USD",
        [new PurchaseOrderLine(1, "Widgets", 100, new Money(20m, "USD"))]);
    await purchaseOrders.AddAsync(purchaseOrder, cancellationToken);

    var command = new SubmitInvoiceCommand(
        supplierId,
        InvoiceNumber: "INV-0001",
        Currency: "USD",
        Lines: [new InvoiceLineItem(PurchaseOrderLineNumber: 1, BilledQuantity: 100, UnitPrice: new Money(20m, "USD"))]);

    var invoiceId = await submitInvoice.ExecuteAsync(
        command,
        new PurchaseOrderReference(poId.Value),
        MatchingPolicy.Default("USD"),
        cancellationToken);

    return Results.Ok(new { purchaseOrderId = poId.Value, invoiceId = invoiceId.Value });
});

app.Run();

public partial class Program;
