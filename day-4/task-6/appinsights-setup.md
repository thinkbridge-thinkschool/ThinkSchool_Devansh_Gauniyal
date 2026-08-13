# App Insights connection setup

**Status: pending** — no Azure subscription available to create the actual resources against (see README.md). This is the real code, ready to use once a resource exists.

## Packages (`QuotesApi.csproj`)

```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.6.0" />
<PackageReference Include="Azure.Identity" Version="1.21.0" />
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.11.0" />
```

(The task text names `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` — that package doesn't exist on NuGet; the real one is `Azure.Monitor.OpenTelemetry.AspNetCore`, confirmed by a 404 on the literal name.)

## Wiring (`Program.cs`)

```csharp
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Security.KeyVault.Secrets;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName));

openTelemetryBuilder.WithTracing(tracing =>
{
    tracing.AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();

    if (builder.Environment.IsDevelopment())
    {
        tracing.AddOtlpExporter(); // local Jaeger/Aspire, Development only
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
        throw new InvalidOperationException(
            "KeyVault:Name must be configured to resolve the Application Insights " +
            "connection string outside the Development/Testing environments.");
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
            "Failed to retrieve the Application Insights connection string from Key " +
            "Vault. Check that the vault is reachable, DefaultAzureCredential can " +
            "authenticate, and the secret exists.");
    }
}
```

## Configuration (not secret — the vault name only)

```json
{
  "KeyVault": {
    "Name": "YOUR_KEY_VAULT_NAME"
  }
}
```

## Creating the secret (once a Key Vault exists)

```
az keyvault secret set \
  --vault-name YOUR_KEY_VAULT_NAME \
  --name ApplicationInsights-ConnectionString \
  --value "YOUR_APP_INSIGHTS_CONNECTION_STRING"
```

`DefaultAzureCredential` uses the local `az login` identity when developing, and a managed identity automatically once deployed — no credential material appears in configuration or code either way.
