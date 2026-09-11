using System.Diagnostics;
using System.Threading.RateLimiting;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
// no UI - see capstone/README.md, "what's deliberately not built yet". The two
// original demo POST/GET endpoints and the two Day 26 endpoints exist only to give
// the wiring, and OpenTelemetry's SQL/Service Bus instrumentation, something real
// to prove against - they are not the deliverable and not a real API shape.
//
// Day 27: the two /v1/* endpoints below ARE a real, if minimal, API shape - added
// specifically because there wasn't one yet to harden (see
// capstone/submission-day-27-task-1.md, "the API surface was thin"). They use the
// same use cases/domain as the demo endpoint, just driven by real request bodies
// instead of hardcoded values, with authentication, versioning, and input limits
// applied - see below.
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

// Day 27 - authentication (STRIDE: Spoofing, Elevation of privilege - see
// capstone/THREAT-MODEL.md). Auth:TenantId/Auth:ApiAppId are non-secret
// identifiers (a tenant GUID, an app registration's GUID) set as app settings by
// infra/modules/api.bicep in every deployed environment. They are deliberately
// absent from appsettings.Development.json - see that file's own comment - so a
// plain local `dotnet run` (per README.md's existing instructions) keeps working
// unauthenticated; only a deployed environment, which always sets both, actually
// enforces this. This is a real, named trade-off, not an oversight: it keeps the
// local demo flow working without requiring a live Entra ID token for a
// throwaway `curl` command, at the cost of local runs not exercising the same
// auth path production does.
var tenantId = builder.Configuration["Auth:TenantId"];
var apiAppId = builder.Configuration["Auth:ApiAppId"];
var authConfigured = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(apiAppId);

if (authConfigured)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            // Both forms observed in real tokens from this app registration: a
            // client-credentials (app-only) token's `aud` is the bare app id GUID;
            // a delegated (user) token's `aud` would be the api://<appId> URI set
            // as this app's Application ID URI. Accepting both, rather than
            // guessing one, is what was actually verified live against this
            // tenant - see submission-day-27-task-1.md.
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudiences = [apiAppId, $"api://{apiAppId}"],
            };
        });
    builder.Services.AddAuthorization();
}

// Day 27 - rate limiting (STRIDE: Denial of service). One global fixed-window
// limiter, partitioned per client IP, applied to every route in this host - see
// THREAT-MODEL.md's Denial-of-service section for why this is per-instance, not a
// global cap across the whole App Service plan.
const int RateLimitPermitsPerWindow = 100;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RateLimitPermitsPerWindow,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// Day 27 - a real OpenAPI document (see Capstone.Web.csproj's comment on why this
// package was previously removed and why it's safe to add back now), versioned
// "v1" to match the /v1 route group below, with the bearer scheme this API
// actually requires declared on it rather than left undocumented.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Capstone Invoicing API";
        document.Info.Version = "v1";
        if (authConfigured)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "An Entra ID access token issued for this API's app registration.",
            };
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Day 27 - security headers, added after a real OWASP ZAP baseline scan against
// this deployed app flagged their absence (see submission-day-27-task-1.md for
// the before/after report). `UseHsts()` is ASP.NET Core's own middleware for
// Strict-Transport-Security; the other three have no framework equivalent for a
// minimal API host, so one small middleware sets them directly. This app is
// always served over HTTPS (httpsOnly: true, modules/api.bicep), so HSTS is
// unconditional rather than gated on environment.
app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Cross-Origin-Resource-Policy", "same-origin");
    // This host serves no content that should ever be cached by a shared or
    // browser cache - every response is either a live domain query result or an
    // auth failure, never something safe to replay stale. The exact three
    // directives ZAP's own "Re-examine Cache-control Directives" [10015] rule
    // names as its suggested fix, not just "no-store" alone.
    context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
    await next();
});

app.UseRateLimiter();

if (authConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapOpenApi();

app.MapGet("/", () => Results.Ok(new { message = "Capstone host is running." }));

// Seeds a single in-memory purchase order and submits one invoice against it, so a
// visitor can see the full Submit -> reserve-capacity round trip actually execute
// through both modules, without a database. Demo scaffolding, not a real endpoint
// shape - see README.md. Left unauthenticated and unversioned deliberately: it
// predates this day's real /v1 surface and still needs to work exactly as
// README.md already documents it.
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
//
// Day 27: this now touches a private-endpoint-only SQL server (see
// submission-day-27-task-1.md) and requires authentication when configured - both
// changes make it a meaningful DoS/information-disclosure target, per
// THREAT-MODEL.md, not just a demo route.
var apiActivitySource = new ActivitySource("Capstone.Api");

var dbPing = app.MapGet("/demo/db-ping", async (IConfiguration configuration, CancellationToken cancellationToken) =>
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
if (authConfigured)
{
    dbPing.RequireAuthorization();
}

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
var traceWorker = app.MapPost("/demo/trace-worker", async (IConfiguration configuration, CancellationToken cancellationToken) =>
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
if (authConfigured)
{
    traceWorker.RequireAuthorization();
}

// ---------------------------------------------------------------------------
// Day 27 - the real, versioned API surface. Two endpoints: creating the
// purchase-order capacity an invoice submits against, and submitting an
// invoice against it - the same domain flow the demo endpoint above hardcodes,
// now driven by a real request body with the input limits below, instead of
// fixed values. See THREAT-MODEL.md's Information-disclosure section for what
// is deliberately still open here (no per-caller filtering by counterparty).
// ---------------------------------------------------------------------------

const int MaxRequestBodyBytes = 16 * 1024;
const int MaxLineCount = 50;
const int MaxInvoiceNumberLength = 64;
const int MaxItemReferenceLength = 200;
const int MaxQuantity = 100_000;
const decimal MaxUnitPrice = 1_000_000m;
const int MaxPageSize = 100;

// A minimal-API endpoint filter, not an MVC RequestSizeLimitAttribute (those only
// apply to controller actions) - rejects a request whose declared Content-Length
// exceeds the limit before the body is ever bound/read. A chunked request with no
// Content-Length is not caught by this check; ASP.NET Core's own default Kestrel
// limit (30 MB) is still the backstop for that case - named here, not silently
// assumed away.
static async ValueTask<object?> EnforceMaxBodySize(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    if (context.HttpContext.Request.ContentLength is long contentLength && contentLength > MaxRequestBodyBytes)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    return await next(context);
}

var v1 = app.MapGroup("/v1");
if (authConfigured)
{
    v1.RequireAuthorization();
}

v1.MapPost("/purchase-orders", async (
    CreatePurchaseOrderRequest request,
    IPurchaseOrderRepository purchaseOrders,
    CancellationToken cancellationToken) =>
{
    var errors = ValidatePurchaseOrderRequest(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var lines = request.Lines
        .Select(l => new PurchaseOrderLine(l.LineNumber, l.ItemReference, l.OrderedQuantity, new Money(l.UnitPrice, request.Currency)))
        .ToList();

    var purchaseOrder = PurchaseOrder.Issue(PurchaseOrderId.New(), request.VendorId, request.BuyerId, request.Currency, lines);
    await purchaseOrders.AddAsync(purchaseOrder, cancellationToken);

    return Results.Created($"/v1/purchase-orders/{purchaseOrder.Id.Value}", new { purchaseOrderId = purchaseOrder.Id.Value });
})
.AddEndpointFilter(EnforceMaxBodySize);

v1.MapPost("/invoices", async (
    SubmitInvoiceRequest request,
    SubmitInvoiceUseCase submitInvoice,
    CancellationToken cancellationToken) =>
{
    var errors = ValidateSubmitInvoiceRequest(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var command = new SubmitInvoiceCommand(
        request.SupplierId,
        request.InvoiceNumber,
        request.Currency,
        [.. request.Lines.Select(l => new InvoiceLineItem(l.PurchaseOrderLineNumber, l.BilledQuantity, new Money(l.UnitPrice, request.Currency)))]);

    try
    {
        var invoiceId = await submitInvoice.ExecuteAsync(
            command,
            new PurchaseOrderReference(request.PurchaseOrderId),
            MatchingPolicy.Default(request.Currency),
            cancellationToken);

        return Results.Created($"/v1/invoices/{invoiceId.Value}", new { invoiceId = invoiceId.Value });
    }
    catch (InvalidOperationException ex)
    {
        // A domain rule rejected this request (unknown PO, wrong vendor, capacity
        // exceeded, etc.) - a real, expected outcome of bad input, not a server
        // fault, so it is reported as 409 rather than an unhandled 500.
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
})
.AddEndpointFilter(EnforceMaxBodySize);

v1.MapGet("/invoices", async (
    IInvoiceRepository invoices,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 20) =>
{
    // Input limits (STRIDE: Denial of service) - an unbounded page size is a
    // classic way to force a single request to scan/return everything.
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

    var results = await invoices.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);
    var items = results.Select(invoice => new
    {
        invoiceId = invoice.Id.Value,
        invoice.InvoiceNumber,
        status = invoice.Status.ToString(),
        invoice.Currency,
        total = invoice.Total.Amount,
        invoice.DueDate,
    });

    return Results.Ok(new { page, pageSize, items });
});

app.Run();

static Dictionary<string, string[]> ValidatePurchaseOrderRequest(CreatePurchaseOrderRequest request)
{
    var errors = new Dictionary<string, List<string>>();
    void AddError(string key, string message) =>
        (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);

    if (request.VendorId == Guid.Empty)
    {
        AddError(nameof(request.VendorId), "VendorId is required.");
    }

    if (request.BuyerId == Guid.Empty)
    {
        AddError(nameof(request.BuyerId), "BuyerId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
    {
        AddError(nameof(request.Currency), "Currency must be a 3-letter ISO code.");
    }

    if (request.Lines is null || request.Lines.Count == 0)
    {
        AddError(nameof(request.Lines), "At least one line is required.");
    }
    else if (request.Lines.Count > MaxLineCount)
    {
        AddError(nameof(request.Lines), $"No more than {MaxLineCount} lines are allowed.");
    }
    else
    {
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.ItemReference) || line.ItemReference.Length > MaxItemReferenceLength)
            {
                AddError(nameof(line.ItemReference), $"Item reference must be 1-{MaxItemReferenceLength} characters.");
            }

            if (line.OrderedQuantity is <= 0 or > MaxQuantity)
            {
                AddError(nameof(line.OrderedQuantity), $"Ordered quantity must be between 1 and {MaxQuantity}.");
            }

            if (line.UnitPrice is <= 0 or > MaxUnitPrice)
            {
                AddError(nameof(line.UnitPrice), $"Unit price must be greater than 0 and no more than {MaxUnitPrice}.");
            }
        }
    }

    return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}

static Dictionary<string, string[]> ValidateSubmitInvoiceRequest(SubmitInvoiceRequest request)
{
    var errors = new Dictionary<string, List<string>>();
    void AddError(string key, string message) =>
        (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);

    if (request.PurchaseOrderId == Guid.Empty)
    {
        AddError(nameof(request.PurchaseOrderId), "PurchaseOrderId is required.");
    }

    if (request.SupplierId == Guid.Empty)
    {
        AddError(nameof(request.SupplierId), "SupplierId is required.");
    }

    if (string.IsNullOrWhiteSpace(request.InvoiceNumber) || request.InvoiceNumber.Length > MaxInvoiceNumberLength)
    {
        AddError(nameof(request.InvoiceNumber), $"InvoiceNumber must be 1-{MaxInvoiceNumberLength} characters.");
    }

    if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
    {
        AddError(nameof(request.Currency), "Currency must be a 3-letter ISO code.");
    }

    if (request.Lines is null || request.Lines.Count == 0)
    {
        AddError(nameof(request.Lines), "At least one line is required.");
    }
    else if (request.Lines.Count > MaxLineCount)
    {
        AddError(nameof(request.Lines), $"No more than {MaxLineCount} lines are allowed.");
    }
    else
    {
        foreach (var line in request.Lines)
        {
            if (line.BilledQuantity is <= 0 or > MaxQuantity)
            {
                AddError(nameof(line.BilledQuantity), $"Billed quantity must be between 1 and {MaxQuantity}.");
            }

            if (line.UnitPrice is <= 0 or > MaxUnitPrice)
            {
                AddError(nameof(line.UnitPrice), $"Unit price must be greater than 0 and no more than {MaxUnitPrice}.");
            }
        }
    }

    return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}

public sealed record CreatePurchaseOrderLineRequest(int LineNumber, string ItemReference, int OrderedQuantity, decimal UnitPrice);

public sealed record CreatePurchaseOrderRequest(Guid VendorId, Guid BuyerId, string Currency, List<CreatePurchaseOrderLineRequest> Lines);

public sealed record SubmitInvoiceLineRequest(int PurchaseOrderLineNumber, int BilledQuantity, decimal UnitPrice);

public sealed record SubmitInvoiceRequest(Guid PurchaseOrderId, Guid SupplierId, string InvoiceNumber, string Currency, List<SubmitInvoiceLineRequest> Lines);

public partial class Program;
