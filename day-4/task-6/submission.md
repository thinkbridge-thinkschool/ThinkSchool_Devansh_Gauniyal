# Day 4 — Task 6: Connect to Azure App Insights

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-6/day-4/task-6

## Notes for your mentor

An Azure subscription ("Azure for Students") became available partway through this task, so everything below is real: resource group `thinkschool-day4-task6` (Central India), a workspace-based Application Insights resource, an RBAC-authorized Key Vault holding the real connection string (never displayed — see README.md for the one place this slipped during setup and how it was fixed before any real use), the app run locally against both, 24 real HTTP requests plus a real custom span ingested, both KQL queries run for real, and a real log-based alert rule (not a metric alert — see below) wired to an action group. All 61+1 tests still pass.

Four corrections along the way, all in `README.md`'s "What actually happened": (1) workspace-based App Insights needs the `AIWorkspacePreview` feature registered first; (2) the task names a package and method (`Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` / `AddAzureMonitor()`) that don't exist — the real ones are `Azure.Monitor.OpenTelemetry.AspNetCore` and `UseAzureMonitor()`; (3) `DefaultAzureCredential` doesn't fall back to `az login` when `ManagedIdentityCredential`'s IMDS probe times out (rather than fails fast) off-Azure — fixed with a small opt-in flag, off by default, that doesn't touch real deployed behavior; (4) the task's suggested metric alert on `requests/duration` can't actually filter to `POST /api/quotes` — that metric has no per-endpoint dimension — so a log alert (real KQL query on a schedule) was used instead.

## App Insights connection setup

```csharp
var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName));

openTelemetryBuilder.WithTracing(tracing =>
{
    tracing.AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();

    if (builder.Environment.IsDevelopment())
    {
        tracing.AddOtlpExporter(); // local Jaeger, Development only
    }
});

if (!builder.Environment.IsEnvironment("Testing") && !builder.Environment.IsDevelopment())
{
    var connectionString = ResolveAppInsightsConnectionString(builder.Configuration);
    openTelemetryBuilder.UseAzureMonitor(options => options.ConnectionString = connectionString);
}

static string ResolveAppInsightsConnectionString(IConfiguration configuration)
{
    const string ConnectionStringSecretName = "ApplicationInsights-ConnectionString";
    var vaultName = configuration["KeyVault:Name"];
    if (string.IsNullOrWhiteSpace(vaultName))
    {
        throw new InvalidOperationException("KeyVault:Name must be configured.");
    }

    // Off by default; real local testing found DefaultAzureCredential doesn't fall
    // back to az login when ManagedIdentityCredential's IMDS probe times out instead
    // of failing fast off-Azure. See README.md for the full story.
    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential =
            configuration.GetValue<bool>("KeyVault:ExcludeManagedIdentityCredential")
    };

    var client = new SecretClient(
        new Uri($"https://{vaultName}.vault.azure.net/"),
        new DefaultAzureCredential(credentialOptions));

    try
    {
        return client.GetSecret(ConnectionStringSecretName).Value.Value;
    }
    catch (Exception exception) when (exception is not InvalidOperationException)
    {
        throw new InvalidOperationException(
            "Failed to retrieve the Application Insights connection string from Key Vault.");
    }
}
```

## KQL query — slowest 10 requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, duration, resultCode, operation_Id
```

Run for real against 24 ingested requests — slowest was 31.87ms (`GET /`), all well under any latency concern for an in-memory API. Full result table, plus the task body's `traces`/`customDimensions` example (which genuinely returns zero rows here, for a real architectural reason — Serilog owns the logger pipeline, so `ILogger` logs never reach Azure Monitor), in `kql-queries.md`.

## What did you learn this session?

A subscription became available mid-task, so everything here ended up real: resources created, real telemetry ingested, both KQL queries run for real, a real log-based alert wired up.
Along the way: the task's package/method names, its suggested metric alert, and even `DefaultAzureCredential`'s local fallback behavior all needed a real fix, not just documentation — see README.md.

## What would break this?

The app deliberately fails to start if Key Vault is unreachable or the RBAC assignment is missing, rather than starting with telemetry silently off.
Structured `ILogger` logs never reach Application Insights today (Serilog owns the pipeline) — anyone relying on `traces` for this app's login/refresh log lines would be looking at an empty table.
