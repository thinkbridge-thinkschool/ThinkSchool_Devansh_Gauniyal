# Day 4 — Task 6: Connect to Azure App Insights

## Status: complete — real Azure resources created and verified

An Azure subscription ("Azure for Students") became available partway through this task. Every resource described below was actually created, the app was actually run locally against them, real telemetry was actually ingested, and both KQL queries were actually run against real data. Nothing in this document is invented — where something didn't work as the task text assumed (see "What actually happened" below), that's called out explicitly rather than glossed over.

## Real resources created

| Resource | Name | Region |
|---|---|---|
| Resource group | `thinkschool-day4-task6` | Central India |
| Log Analytics workspace | `thinkschool-day4-task6-law` | Central India |
| Application Insights (workspace-based) | `thinkschool-day4-task6-ai` | Central India |
| Key Vault (Standard, RBAC-authorized) | `thinkschool-day4-t6-kv` | Central India |
| Action group | `thinkschool-day4-task6-ag` | (global) |
| Alert rule (log-based, see correction below) | `thinkschool-day4-task6-quotes-latency` | (global) |

Resource names and region are not secret and are shown as-is. The subscription ID and tenant ID are redacted everywhere as `YOUR_SUBSCRIPTION_ID` / `YOUR_TENANT_ID` per the task's rules. The action group's email receiver is the account owner's own address — redacted below as `YOUR_EMAIL`.

## What actually happened (corrections to the original plan)

Three things came up during real execution that the original documentation-only draft couldn't have anticipated:

**1. Workspace-based Application Insights needs a preview feature registered first.** `az monitor app-insights component create --workspace ...` failed with `ResourceNotFound` until `az feature register --name AIWorkspacePreview --namespace microsoft.insights` was run, followed by `az provider register -n microsoft.insights` to propagate it. Took a couple of minutes to take effect.

**2. A connection-string handling mistake, caught and fixed.** While creating the Application Insights component, `-o table` was used without excluding the `ConnectionString` column, and that output was briefly written to a local scratch log file and then read back — surfacing the real connection string in this session, in violation of the "never display it" rule. As soon as this was noticed: the log file was deleted, and — since the connection string can't be rotated via a simple API call (unlike, say, a storage account key) — the Application Insights component was deleted and recreated from scratch before any telemetry had flowed, giving a connection string that was never exposed. Every command touching the real connection string afterward used `--query`/`-o none` and piped values directly between commands without ever printing them.

**3. `ManagedIdentityCredential`'s IMDS probe doesn't fail fast on this machine, which breaks `DefaultAzureCredential`'s fallback to `az login`.** Locally (not on an actual Azure VM), the probe to the Azure Instance Metadata Service (`169.254.169.254`) doesn't get a quick "connection refused" — the packet is silently dropped, so the probe hangs until a ~3-minute timeout. Azure.Identity classifies that specific timeout as a fatal `AuthenticationFailedException` rather than "try the next credential in the chain," which meant `DefaultAzureCredential` never reached `AzureCliCredential` (the developer's `az login` session) at all, and the app failed to start. Confirmed the root cause with a standalone probe program before touching any shipped code. Fixed with a small, additive, opt-in change — see "Connection string from Key Vault" below.

**4. The task's suggested metric alert doesn't work as written.** `requests/duration` (the standard Azure Monitor *platform metric* for Application Insights) has no per-endpoint dimension — its only dimensions are `resultCode`, `success`, `performanceBucket`, `roleName`, `roleInstance` (confirmed via `az monitor metrics list-definitions`). There is no way to filter that metric down to just `POST /api/quotes`. The correct mechanism for endpoint-specific latency alerting in Application Insights is a **log alert** (`az monitor scheduled-query create`) running a real KQL query against the `requests` table, not a metric alert. Used that instead — see "The alert" below.

## Resources this task creates, and what they cost

Reviewed and approved resource-by-resource before creation. Check current prices yourself before relying on these figures.

| Resource | What it is | Why this task needs it | Free allowance | What triggers a charge |
|---|---|---|---|---|
| Application Insights (+ Log Analytics workspace) | Telemetry ingestion/storage/query backend | Receives logs/metrics/traces from `UseAzureMonitor()` | Yes, a monthly data-ingestion allowance | Ingested data volume beyond the free allowance. Pricing: https://azure.microsoft.com/pricing/details/monitor/ |
| Azure Key Vault (Standard tier) | Secret storage | Holds the App Insights connection string, retrieved via `DefaultAzureCredential` — never hardcoded | Operations have a free-tier allowance on Standard | Per-operation cost beyond the free allowance. Pricing: https://azure.microsoft.com/pricing/details/key-vault/ |
| Metric/log alert rule + action group (email) | Watches `POST /api/quotes` average response time, notifies on breach | The task's alerting requirement | A small number of free alert rules; action groups have their own free tier | Alert rules/notifications beyond the free allowance. Pricing: https://azure.microsoft.com/pricing/details/monitor/ |

Actual configuration used: Key Vault Standard tier (RBAC-authorized, not access policies), workspace-based Application Insights, one log alert with one email action. Real usage here was trivially small (single-digit-minute test run, ~24 requests) — well inside every free allowance.

## Manual Azure steps — what was actually run

**(a) Created the Application Insights resource** (workspace-based, after registering `AIWorkspacePreview`):

```
az monitor log-analytics workspace create \
  --resource-group thinkschool-day4-task6 \
  --workspace-name thinkschool-day4-task6-law \
  --location centralindia

az monitor app-insights component create \
  --app thinkschool-day4-task6-ai \
  --location centralindia \
  --resource-group thinkschool-day4-task6 \
  --workspace <log-analytics-workspace-resource-id> \
  --application-type web
```

**(b) Created the Key Vault, stored the connection string as a secret** — RBAC-authorized (not access policies):

```
az keyvault create \
  --name thinkschool-day4-t6-kv \
  --resource-group thinkschool-day4-task6 \
  --location centralindia \
  --sku standard \
  --enable-rbac-authorization true
```

The connection string was captured into a shell variable and piped directly into `az keyvault secret set ... -o none` in one step — never printed, logged, or written to a file (see the exposure-and-fix note above for the one place this slipped, and how it was corrected before any real use).

**(c) Granted RBAC access to the vault:**

```
az role assignment create \
  --role "Key Vault Secrets Officer" \
  --assignee-object-id <own-aad-object-id> \
  --assignee-principal-type User \
  --scope <key-vault-resource-id>
```

`DefaultAzureCredential` picks this up locally via the `az login` session (`AzureCliCredential`); a deployed instance would instead use a managed identity granted the same role — no credential material anywhere in configuration or code either way.

**(d) Ran the app locally in a simulated non-Development environment** (`ASPNETCORE_ENVIRONMENT=Production`, pointed at the real Key Vault) and exercised it: login, `/api/protected`, `GET`/`POST`/`PUT`/`DELETE` on `/api/quotes`, a refresh-token rotation, and one intentionally unauthorized request — 24 real HTTP requests ingested into Application Insights within a few minutes, plus one real custom `refresh-token.rotate` span (see "Telemetry, verified end-to-end" below).

**(e) Created the alert rule and its action group** (see "The alert" section for the corrected approach):

```
az monitor action-group create \
  --resource-group thinkschool-day4-task6 \
  --name thinkschool-day4-task6-ag \
  --short-name t6alert \
  --action email owner YOUR_EMAIL

az monitor scheduled-query create \
  --name thinkschool-day4-task6-quotes-latency \
  --resource-group thinkschool-day4-task6 \
  --scopes <app-insights-resource-id> \
  --condition 'avg "AggregatedValue" from "Query1" > 500' \
  --condition-query Query1='requests | where name == "POST /api/quotes" | summarize AggregatedValue = avg(duration) by bin(timestamp, 5m)' \
  --window-size 5m \
  --evaluation-frequency 1m \
  --severity 3 \
  --action-groups <action-group-resource-id> \
  --description "Average response time for POST /api/quotes exceeded 500ms over 5 minutes."
```

## What changed and where

All changes are in `day-3/task-3/QuotesApi`:

- `QuotesApi.csproj` — added `Azure.Monitor.OpenTelemetry.AspNetCore` (the real package name — the task text says `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore`, which doesn't exist on NuGet; confirmed by a 404), plus `Azure.Identity` and `Azure.Security.KeyVault.Secrets`.
- `Program.cs` — the exporter decision, Key Vault-backed connection string resolution (plus the local-verification escape hatch below), and the environment gate that keeps both entirely out of tests.

New test project `day-4/task-6/QuotesApi.Telemetry.Tests` (`ProjectReference` only, no Day 3 source duplicated).

## Package name corrections (verified, not assumed)

- The package is **`Azure.Monitor.OpenTelemetry.AspNetCore`**, not `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` (404 on the literal name).
- The extension method is **`UseAzureMonitor()`**, not `AddAzureMonitor()` — confirmed by inspecting the compiled assembly's exported method names and by compiling a probe program against it.

## Version compatibility (checked, not assumed)

`Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` depends on `OpenTelemetry.Extensions.Hosting`/`Instrumentation.AspNetCore`/`Instrumentation.Http` **1.15.x**, while Task 5 already pinned all three to **1.17.0**. Ran the actual restore and `dotnet list package` afterward — all three resolved cleanly to `1.17.0`, no `NU1605` downgrade warning.

## The exporter decision

Task 5 configured `AddOtlpExporter()` for a local Jaeger/Aspire dashboard. Running that alongside Azure Monitor in every environment would mean every span exports twice. Chosen instead: **OTLP stays a `Development`-only convenience; Azure Monitor is the exporter everywhere else** (any environment that is neither `"Testing"` nor `"Development"`). Exactly one exporter is ever active per environment.

```csharp
openTelemetryBuilder.WithTracing(tracing =>
{
    tracing.AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();

    if (builder.Environment.IsDevelopment())
    {
        tracing.AddOtlpExporter();
    }
});

if (!builder.Environment.IsEnvironment("Testing") && !builder.Environment.IsDevelopment())
{
    var connectionString = ResolveAppInsightsConnectionString(builder.Configuration);
    openTelemetryBuilder.UseAzureMonitor(options => options.ConnectionString = connectionString);
}
```

## Connection string from Key Vault — never hardcoded

The security core of this task. The connection string:

- Never appears in `appsettings.json`, any committed file, any code literal, or any log line.
- Is fetched at startup via `Azure.Security.KeyVault.Secrets.SecretClient`, authenticated with `Azure.Identity.DefaultAzureCredential`.
- Is looked up under the fixed secret name **`ApplicationInsights-ConnectionString`**.
- Only the **vault name** is configuration (`"KeyVault:Name"`) — not a secret, but still parameterized rather than baked into code.

**Local-verification escape hatch, added after real testing surfaced the IMDS-timeout issue described above:**

```csharp
var credentialOptions = new DefaultAzureCredentialOptions
{
    ExcludeManagedIdentityCredential =
        configuration.GetValue<bool>("KeyVault:ExcludeManagedIdentityCredential")
};

var client = new SecretClient(
    new Uri($"https://{vaultName}.vault.azure.net/"),
    new DefaultAzureCredential(credentialOptions));
```

Defaults to `false` — zero effect on real deployment, where `ManagedIdentityCredential`'s IMDS probe succeeds in milliseconds against the genuine Azure metadata service. It was set to `true` only via an environment variable (`KeyVault__ExcludeManagedIdentityCredential=true`), only for the local verification run described here, never committed anywhere.

**Failure handling stays explicit and deliberate.** If the vault is unreachable, `DefaultAzureCredential` can't authenticate, or the secret doesn't exist, `ResolveAppInsightsConnectionString` throws a generic `InvalidOperationException` — the *original* exception and its message are deliberately discarded rather than wrapped, so no Key Vault error detail ever reaches a log sink. The app does **not** start with telemetry silently disabled.

## Test isolation from Azure — how it's actually achieved

`ResolveAppInsightsConnectionString` (and therefore Key Vault, and therefore `UseAzureMonitor`) is only ever called when the environment is neither `"Testing"` nor `"Development"`. Every `WebApplicationFactory` in this repo calls `builder.UseEnvironment("Testing")`, so none of them ever reach that code path. Verified two ways:

1. Ran the entire existing test suite after this change — all pass, in a few seconds wall-clock (no hang or retry against an unreachable endpoint).
2. A dedicated regression test (`TestingEnvironment_StartsAndServesRequests_WithoutKeyVaultConfigured`) whose factory deliberately configures **no** `"KeyVault:Name"` at all — a successful `200 OK` is itself the proof the Azure/Key Vault path was never reached.

## Telemetry, verified end-to-end

The app was run locally (`ASPNETCORE_ENVIRONMENT=Production`, pointed at the real Key Vault and Application Insights resource above) and exercised with real HTTP traffic: `GET /`, login, `GET /api/protected`, `GET`/`POST`/`PUT`/`DELETE` on `/api/quotes`, a refresh-token rotation, and one intentional unauthorized request.

Confirmed ingested in Application Insights (via `union requests, dependencies, exceptions, traces, customEvents | summarize count() by itemType`):

| itemType | count |
|---|---|
| request | 24 |
| dependency | 1 |

The one `dependency` row is the custom `refresh-token.rotate` `ActivitySource` span from Task 5 — confirmed by querying it directly: `type: InProc`, `name: refresh-token.rotate`, correctly correlated to its parent request via `operation_Id`. This is real, direct confirmation that the custom span from Task 5 genuinely reaches Azure Monitor, not just the automatic ASP.NET Core instrumentation.

**A genuine finding, not a query mistake: zero rows in `traces`.** The task's own example query targets `traces` (see `kql-queries.md`), and running it for real against this app returns nothing — for a real architectural reason, not a fluke. `Program.cs` calls `builder.Host.UseSerilog(...)`, which replaces the ASP.NET Core `ILoggerFactory` entirely with Serilog's own pipeline. Every `_logger.LogInformation(...)` call in this codebase (the login/refresh log lines with `{UserId}`/`{FamilyId}` from Task 4) goes through Serilog's own sinks (console only, in this app) — never through the OpenTelemetry Logs pipeline that `UseAzureMonitor()` sets up. Only `ActivitySource`/tracing-based telemetry (requests, the custom `refresh-token.rotate` span) reaches Application Insights; structured application logs currently do not. That's worth knowing before relying on `traces` for anything this app logs via `ILogger` — see `kql-queries.md` for the full query and result.

## PII re-audit

Once Azure Monitor is active, every log property and span tag genuinely leaves the machine for a cloud service:

- The only structured properties logged anywhere are `UserId`, `FamilyId` (a GUID), and `LifetimeSeconds` — no password, token, refresh token, JWT, or `Authorization` header. (As noted above, none of these actually reach Application Insights today, since Serilog owns the logging pipeline — but the audit stands regardless, in case that ever changes.)
- The only `SetTag` calls are `user.id` and `refresh_token.outcome` — these *do* reach Azure Monitor, via the custom span.
- No `EnrichWithHttpRequest`/`EnrichWithHttpResponse` callback captures headers or bodies.
- **Flagging, not concluding**: `user.id` is PII, genuinely leaving this machine into a cloud service (this Application Insights resource, in Central India) once telemetry flows. Retention period, data residency, and query access control deserve a real compliance review before this goes near production data — this is a flag for that review, not a compliance conclusion.

## KQL queries

See `kql-queries.md` for both queries, full explanations, and real result data (10 real rows for the first query; the empty-but-explained result for the second).

## The alert

The task's example targets `POST /api/quotes`, which genuinely exists in this API (confirmed by grepping `Program.cs`). As covered in "What actually happened" above, the task's suggested *metric* alert on `requests/duration` doesn't support filtering by endpoint — that metric has no per-request-name dimension. Created a **log alert** instead (`az monitor scheduled-query create`), which runs a real KQL query on a schedule:

```kql
requests
| where name == "POST /api/quotes"
| summarize AggregatedValue = avg(duration) by bin(timestamp, 5m)
```

— firing when `AggregatedValue > 500` (ms), evaluated every 1 minute over a 5-minute window, notifying the action group by email. Real, created, enabled (`severity: 3`). It has not fired and is not expected to: this app's real traffic averaged ~3ms for `POST /api/quotes` (see `kql-queries.md`), since the handler does no I/O of its own (in-memory repository). A sustained 500ms average here would be a genuinely unusual, worth-investigating signal.

## Teardown

Delete everything in one command by removing the resource group:

```
az group delete --name thinkschool-day4-task6 --yes --no-wait
```

`--no-wait` returns immediately; the group and everything inside it (Application Insights, Log Analytics workspace, Key Vault, action group, alert rule) deletes asynchronously in the background.

**As of writing, these resources still exist** — real usage so far is trivial and well within every free allowance, but they are live and billable-if-usage-grows until this command is run.

## What is safe to share vs. secret (recap)

- **Secret, never share**: the App Insights connection string (contains an ingestion key) — never in a file, commit, screenshot, URL, or log. (See "What actually happened" above for the one place this briefly slipped during setup, and the fix applied.)
- **Redact from every file/example**: tenant ID, subscription ID, client/application ID, email address → `YOUR_TENANT_ID`, `YOUR_SUBSCRIPTION_ID`, `YOUR_CLIENT_ID`, `YOUR_EMAIL`.
- **Safe to share**: resource names, region, the KQL queries and their result data, the Key Vault *name* (not its contents).

One more thing worth flagging plainly: `az account show` returned a tenant ID that is the *exact same value* already hardcoded as `"Entra:TenantId"` in `day-3/task-3/QuotesApi/appsettings.json` (committed in Day 3, not part of this task's changes). That looks like a real tenant ID sitting in a shared repo rather than a synthetic placeholder — not fixed here since it's out of this task's approved scope, but worth your own follow-up.
