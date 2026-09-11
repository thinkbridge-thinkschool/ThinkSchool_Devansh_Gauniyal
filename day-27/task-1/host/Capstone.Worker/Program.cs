using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Capstone.Worker;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Day 26: same reasoning as host/Capstone.Web/Program.cs - MUST be set before any
// Azure SDK client is constructed. See that file's comment for the full
// explanation; duplicated here (not shared) because this project has no reference
// to Capstone.Web and shouldn't gain one just to share three lines.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

// Day 26 - hosting pivot: this started as a plain console/Generic-Host app meant
// to run as a continuous WebJob inside Capstone.Web's App Service. That failed for
// real - Kudu's continuous-WebJobs manager on Linux ("KuduLite") refused to run it
// even after the files landed correctly (`{"error":"The web app is not configured
// to run the web job..."}`, HTTP 409, on every retry including with
// WEBSITES_ENABLE_APP_SERVICE_STORAGE=true) - a documented, real gap in Linux
// WebJobs support, not a configuration mistake on this project's part. See
// ../../infra/README.md's verification log for the exact error and what was tried.
// This is now a second, minimal Linux Web App on the SAME App Service plan and
// identity as Capstone.Web (infra/modules/worker.bicep) - which is why it's
// WebApplication, not a bare Host: App Service's own platform health probe needs
// something to answer on the assigned port, which a pure background console app
// never provided. The one HTTP endpoint below (`/health`) exists ONLY for that
// probe - the real work is still the BackgroundService registered below.
var builder = WebApplication.CreateBuilder(args);

var azureCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"],
});

var samplingRatio = float.TryParse(builder.Configuration["OTEL_SAMPLING_RATIO"], out var parsedRatio)
    ? parsedRatio
    : 1.0F;

// Day 26: this is the ".NET" (non-ASP.NET Core) shape of Azure Monitor
// OpenTelemetry wiring - AddAzureMonitorTraceExporter / AddAzureMonitorMetricExporter
// / AddAzureMonitorLogExporter, each taking their own Credential, rather than the
// single UseAzureMonitor() call Capstone.Web uses. Confirmed live against
// https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication
// on 2026-09-10 - the ".NET" tab there is this exact three-provider shape, not the
// ASP.NET Core Distro's one-call shape, because this project has no ASP.NET Core
// host to hang a single AddOpenTelemetry().UseAzureMonitor() off of.
var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("APPLICATIONINSIGHTS_CONNECTION_STRING is not configured.");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "capstone-worker"))
    .WithTracing(tracing => tracing
        .AddSource("Capstone.Worker")
        .AddSource("Azure.Messaging.ServiceBus.*")
        .AddSqlClientInstrumentation()
        .SetSampler(new TraceIdRatioBasedSampler(samplingRatio))
        .AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = connectionString;
            options.Credential = azureCredential;
        }))
    .WithMetrics(metrics => metrics
        .AddAzureMonitorMetricExporter(options =>
        {
            options.ConnectionString = connectionString;
            options.Credential = azureCredential;
        }));

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddAzureMonitorLogExporter(options =>
    {
        options.ConnectionString = connectionString;
        options.Credential = azureCredential;
    });
});

// Day 26 - what actually broke: AddSingleton(azureCredential) alone registers it
// under its concrete runtime type (DefaultAzureCredential), but
// TraceDemoWorker's constructor asks for the base Azure.Core.TokenCredential -
// DI can't find a registration for that type, so the host aborts (SIGABRT,
// "Aborted (core dumped)") on every startup attempt before ever reaching
// ExecuteAsync. Confirmed via the container's own crash log
// (StartupLogs/*_failure.log: "Unable to resolve service for type
// 'Azure.Core.TokenCredential'"), not assumed. Explicit <TokenCredential> fixes it.
builder.Services.AddSingleton<TokenCredential>(azureCredential);
builder.Services.AddHostedService<TraceDemoWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
