using System.Diagnostics;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Capstone.Invoicing.Application.Ports;
using Capstone.Invoicing.Application.UseCases;
using Capstone.Invoicing.Domain;
using Capstone.Invoicing.Infrastructure;
using Capstone.Procurement.Application;
using Capstone.Procurement.Domain;
using Capstone.Procurement.Infrastructure;
using Capstone.SharedKernel;
using Microsoft.Data.SqlClient;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Day 26: MUST be set before any Azure SDK client (ServiceBusClient below, and
// anything Microsoft.Data.SqlClient does internally) is constructed - Service Bus
// tracing is still "experimental" in Azure.Core as of the versions pinned in this
// project's .csproj (verified live against
// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/samples/Diagnostics.md
// on 2026-09-10: "messaging libraries remain experimental and require explicit
// enablement" - unlike most other Azure SDK libraries, this is NOT on by default).
// Set this switch too late, or not at all, and Service Bus send/receive spans
// simply never appear - no error, no warning, just a trace that silently stops at
// the send call. See infra/README.md, "Day 26 - what actually broke" for why this
// comment exists.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

// Deliberately thin: this host wires dependency injection and exposes just enough
// to prove the module composition resolves and runs, nothing more. No persistence,
// no auth, no UI - see capstone/README.md, "what's deliberately not built yet". The
// two original POST/GET endpoints exist to demonstrate the wiring end to end (the
// dependency graph really does resolve, the modules really do talk to each other
// only through the port/adapter shown in ProcurementCapacityAdapter.cs); they are
// not the deliverable. The two Day 26 endpoints below (/demo/db-ping,
// /demo/trace-worker) exist ONLY to give OpenTelemetry's SQL and Service Bus
// instrumentation something real to instrument - see infra/README.md, "Day 26 -
// the trace-demo worker" for why these are not, and are not meant to look like,
// the real Invoice persistence/messaging DESIGN.md describes.
var builder = WebApplication.CreateBuilder(args);

// Day 26: OpenTelemetry -> Azure Monitor, traces + metrics + logs in one call.
// Credential: DefaultAzureCredential scoped to this Web App's one user-assigned
// identity (AZURE_CLIENT_ID, set by infra/modules/api.bicep) - not the connection
// string's key. Combined with DisableLocalAuth: true on the Application Insights
// resource (infra/modules/monitoring.bicep), the connection string alone
// (retrieved via the Key Vault reference in APPLICATIONINSIGHTS_CONNECTION_STRING)
// cannot authenticate ingestion by itself; this credential is what actually can.
// Verified live against
// https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication
// on 2026-09-10 for the exact `options.Credential` shape.
var azureCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"],
});

// Sampling: read from OTEL_SAMPLING_RATIO (an app setting - infra/modules/api.bicep),
// default 1.0 (no sampling) for local runs where that setting doesn't exist. See
// infra/README.md, "Day 26 - sampling" for the dev-vs-prod trade-off this encodes.
var samplingRatio = float.TryParse(builder.Configuration["OTEL_SAMPLING_RATIO"], out var parsedRatio)
    ? parsedRatio
    : 1.0F;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "capstone-api"))
    .UseAzureMonitor(options =>
    {
        options.Credential = azureCredential;
        options.SamplingRatio = samplingRatio;
    });

// SqlClient instrumentation is NOT bundled by the Azure Monitor distro (only
// ASP.NET Core + HttpClient are - confirmed live against
// https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-configuration
// on 2026-09-10), so it's added explicitly here as a community instrumentation
// library. `SetDbStatementForText: false` (the default) - this demo's queries
// contain no sensitive data, but capturing raw SQL text is off by default in this
// project as a matter of habit, not because today's queries need hiding.
//
// Azure.Messaging.ServiceBus.* activity source: this is what actually lets the
// OpenTelemetry SDK export the Azure SDK's own send/receive spans once the
// AppContext switch above turns them on - without this AddSource call, the switch
// alone does nothing, because nothing is listening for the ActivitySource it
// enables.
builder.Services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
{
    tracerProviderBuilder
        .AddSqlClientInstrumentation()
        .AddSource("Azure.Messaging.ServiceBus.*")
        .AddSource("Capstone.Api");
});

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

// Day 26: a genuine SQL dependency call, so the OpenTelemetry SqlClient
// instrumentation and the dependency-breakdown KQL query have something real to
// show. `SELECT 1` deliberately - this proves the managed-identity connection
// actually authenticates and round-trips (Day 25's honest limit was "no proof of a
// live TDS query against SQL from this session" - see infra/README.md's
// identity-connectivity-proof.txt). It is NOT the start of a real persistence
// layer; both repositories remain in-memory (see capstone/README.md).
var apiActivitySource = new ActivitySource("Capstone.Api");

app.MapGet("/demo/db-ping", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var connectionString = configuration.GetConnectionString("CapstoneDb")
        ?? throw new InvalidOperationException("ConnectionStrings:CapstoneDb is not configured.");

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1";
    var result = await command.ExecuteScalarAsync(cancellationToken);

    return Results.Ok(new { pinged = true, result, atUtc = DateTimeOffset.UtcNow });
});

// Day 26: sends one message to the trace-demo worker's topic, with the current
// trace context carried explicitly in the message's application properties. This
// is the propagation step the task calls out as the one that most often silently
// fails: Service Bus, as a broker, has no built-in concept of a "parent span" -
// whatever isn't put INTO the message never survives the hop, no matter how well
// instrumented either side is on its own. `traceparent` is the W3C Trace Context
// format (https://www.w3.org/TR/trace-context/) that `Activity.Id` already
// produces under .NET's default (W3C) id format - read explicitly on the worker
// side in host/Capstone.Worker/TraceDemoWorker.cs, not left to any SDK-automatic
// mechanism, so this project can prove propagation instead of assuming it (see
// infra/README.md, "Day 26 - proving propagation across the Service Bus hop").
app.MapPost("/demo/trace-worker", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    using var activity = apiActivitySource.StartActivity("capstone-api.send-trace-demo-message", ActivityKind.Producer);

    var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
        ?? throw new InvalidOperationException("ServiceBus:FullyQualifiedNamespace is not configured.");
    var topicName = configuration["ServiceBus:DemoSubscriptionTopicName"]
        ?? throw new InvalidOperationException("ServiceBus:DemoSubscriptionTopicName is not configured.");

    await using var client = new ServiceBusClient(fullyQualifiedNamespace, azureCredential);
    await using var sender = client.CreateSender(topicName);

    var messageId = Guid.NewGuid().ToString();
    var message = new ServiceBusMessage(
        BinaryData.FromObjectAsJson(new { messageId, sentAtUtc = DateTimeOffset.UtcNow }))
    {
        MessageId = messageId,
    };

    // The explicit propagation step described above.
    if (Activity.Current?.Id is { } traceparent)
    {
        message.ApplicationProperties["traceparent"] = traceparent;
    }

    await sender.SendMessageAsync(message, cancellationToken);

    return Results.Accepted(value: new { messageId, topic = topicName });
});

app.Run();

public partial class Program;
