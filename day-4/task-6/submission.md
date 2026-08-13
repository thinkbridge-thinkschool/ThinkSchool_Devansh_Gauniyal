# Day 4 — Task 6: Connect to Azure App Insights

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-6/day-4/task-6

## Notes for your mentor

No Azure subscription available in this environment — authenticated (`az account show` succeeds) but tenant-level only, confirmed with a real read-only call (`az group list`) failing `SubscriptionNotFound`. Proceeded in documentation-only mode: wrote the real Azure Monitor + Key Vault integration code, verified it builds and all 61 tests pass (60 existing + 1 new isolation test), and marked every actual Azure step (resource creation, KQL run, alert rule) pending — nothing invented. Also caught two things worth flagging: the task names a package and method (`Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` / `AddAzureMonitor()`) that don't actually exist — the real ones are `Azure.Monitor.OpenTelemetry.AspNetCore` and `UseAzureMonitor()`, confirmed against NuGet and the compiled assembly. `POST /api/quotes` (the task's alert-example endpoint) does genuinely exist in this API, so no substitution was needed there.

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

    var client = new SecretClient(
        new Uri($"https://{vaultName}.vault.azure.net/"),
        new DefaultAzureCredential());

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

Pending — no telemetry exists yet to run this against. Full explanation, plus the task body's `traces`/`customDimensions` example, in `kql-queries.md`.

## What did you learn this session?

There's no usable Azure subscription here — authenticated but SubscriptionNotFound on a real call — so everything is real, ready code with each manual step marked pending instead of faking a result.
The task named a package and method that don't actually exist; the real ones are Azure.Monitor.OpenTelemetry.AspNetCore and UseAzureMonitor().

## What would break this?

The app deliberately fails to start if Key Vault is unreachable or the RBAC/access-policy assignment is missing, rather than starting with telemetry silently off.
Rotating the connection string without updating the secret would silently stop telemetry, and an alert on the average could hide a bad p99 while paging on nothing anyone can act on.
