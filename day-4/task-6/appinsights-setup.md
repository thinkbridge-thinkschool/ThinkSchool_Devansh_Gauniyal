# App Insights connection setup

**Status: complete** — real resources created and verified against (see README.md for full details): Application Insights `thinkschool-day4-task6-ai`, Key Vault `thinkschool-day4-t6-kv`, resource group `thinkschool-day4-task6` (Central India).

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

    // Opt-in escape hatch, off by default: on a real Azure VM/App Service deployment,
    // ManagedIdentityCredential's IMDS probe succeeds in milliseconds, so this stays
    // false there and managed identity keeps working exactly as before. Off Azure, a
    // dropped (rather than refused) probe to 169.254.169.254 makes Azure.Identity
    // classify the timeout as a fatal AuthenticationFailedException instead of "try
    // the next credential", which otherwise stops DefaultAzureCredential from ever
    // reaching AzureCliCredential during local verification. Confirmed against this
    // exact vault: real local runs hung for ~3 minutes and then failed outright
    // until this flag was added -- see README.md's "What actually happened" section.
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
    "Name": "thinkschool-day4-t6-kv"
  }
}
```

Real vault name shown as-is (not secret). Not committed to any checked-in `appsettings.*.json` — this app has no real Azure-hosted deployment target yet, so it was supplied via the `KeyVault__Name` environment variable for the local verification run instead.

## Creating the secret (real vault, real secret — value never displayed)

```
CONN=$(az monitor app-insights component show \
  --app thinkschool-day4-task6-ai \
  --resource-group thinkschool-day4-task6 \
  --query connectionString -o tsv)

az keyvault secret set \
  --vault-name thinkschool-day4-t6-kv \
  --name ApplicationInsights-ConnectionString \
  --value "$CONN" \
  -o none

unset CONN
```

Both commands run in one shell invocation so the value only ever exists in a local, never-echoed variable. Verified afterward with `az keyvault secret show ... --query "{name:name, enabled:attributes.enabled}"` — metadata only, never the value.

`DefaultAzureCredential` uses the local `az login` identity when developing (confirmed working, after the `ExcludeManagedIdentityCredential` fix above), and would use a managed identity automatically once deployed — no credential material appears in configuration or code either way.
