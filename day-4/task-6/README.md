# Day 4 — Task 6: Connect to Azure App Insights

## Status: documentation-only mode

There is no usable Azure subscription available in this environment — `az account show` authenticates fine, but the account is tenant-level only (`"N/A(tenant level account)"`), and a basic read-only call (`az group list`) fails with `SubscriptionNotFound`. Every Azure resource, portal step, and query result described below is written correctly but genuinely **not yet created or run** — each is marked pending. Nothing here is fabricated: no connection string, resource, screenshot, or query result exists yet, and none is invented in its place.

## Resources this task would create, and what they cost (informational — confirm current prices yourself)

None of these have been created. When a subscription is available, review this table and approve resource-by-resource before creating anything — don't take these figures on faith, check the official pricing pages linked below, since prices change.

| Resource | What it is | Why this task needs it | Free allowance | What triggers a charge |
|---|---|---|---|---|
| Application Insights (+ a Log Analytics workspace, created alongside it automatically) | Telemetry ingestion/storage/query backend | The task's whole point — receiving logs/metrics/traces from `UseAzureMonitor()` | Yes, a monthly data-ingestion allowance | Ingested data volume beyond the free allowance. Pricing: https://azure.microsoft.com/pricing/details/monitor/ |
| Azure Key Vault (Standard tier) | Secret storage | Holds the App Insights connection string, retrieved via `DefaultAzureCredential` — never hardcoded | Operations have a free-tier allowance on Standard | Per-operation cost beyond the free allowance (get/list/set calls). Pricing: https://azure.microsoft.com/pricing/details/key-vault/ |
| Metric alert rule + action group (email) | Watches the `POST /api/quotes` average-response-time metric, notifies on breach | The task's Step 7 requirement | A small number of free alert rules; action groups have their own free tier | Alert rules and notifications beyond the free allowance. Pricing: https://azure.microsoft.com/pricing/details/monitor/ |

Cheapest configuration: Key Vault Standard tier (not Premium/HSM-backed — no requirement here for hardware-backed keys), App Insights on the default workspace-based pricing tier, one alert rule with one email action.

## Manual Azure steps — exact instructions, all currently PENDING (no subscription to run them against)

For each step: portal click-path, the equivalent `az` command, which values are safe to share, and which are secret.

**(a) Create the Application Insights resource**

- Portal: Create a resource → search "Application Insights" → New → choose subscription, resource group, name, region (workspace-based, which creates/uses a Log Analytics workspace automatically) → Review + create.
- CLI:
  ```
  az monitor app-insights component create \
    --app YOUR_APP_INSIGHTS_NAME \
    --location YOUR_REGION \
    --resource-group YOUR_RESOURCE_GROUP \
    --workspace YOUR_LOG_ANALYTICS_WORKSPACE_ID
  ```
- Safe to share: the resource name, region. **Secret**: the connection string this resource generates (next step covers storing it properly).

**(b) Create the Key Vault, store the connection string as a secret**

- Portal: Create a resource → "Key Vault" → choose subscription, resource group, name (must be globally unique), region, pricing tier **Standard** → Review + create. Then: Objects → Secrets → Generate/Import → name it **exactly** `ApplicationInsights-ConnectionString` (this must match the code) → paste the connection string from step (a) as the value.
- CLI:
  ```
  az keyvault create --name YOUR_KEY_VAULT_NAME --resource-group YOUR_RESOURCE_GROUP --location YOUR_REGION
  az keyvault secret set --vault-name YOUR_KEY_VAULT_NAME \
    --name ApplicationInsights-ConnectionString \
    --value "YOUR_APP_INSIGHTS_CONNECTION_STRING"
  ```
- Safe to share: the vault name, region, the secret *name*. **Secret, never share**: the value you just stored (the connection string itself), and never let it appear in your shell history in a way that gets logged/shared.

**(c) Grant your identity permission to read that secret**

- Two access models exist for Key Vault — **know which one your vault uses, since the click-path differs**:
  - **Azure RBAC** (recommended, newer): role assignments via Access control (IAM). Portal: the Key Vault resource → Access control (IAM) → Add role assignment → "Key Vault Secrets User" → assign to your user/managed identity.
    CLI: `az role assignment create --role "Key Vault Secrets User" --assignee YOUR_CLIENT_ID_OR_USER --scope YOUR_KEY_VAULT_RESOURCE_ID`
  - **Vault access policies** (legacy): Key Vault resource → Access policies → Create → select "Get" + "List" secret permissions → assign to your user/managed identity.
  - Which model a given vault uses is set at creation time (`--enable-rbac-authorization` true/false) and shown on the vault's Access configuration blade — check this before trying to grant access, since using the wrong model's UI won't work.
- Safe to share: which model you chose, the role name. **Redact**: your own object ID / client ID from any screenshot → `YOUR_CLIENT_ID`.

**(d) Run the KQL query in the portal**

- Portal: the App Insights resource → Logs (under Monitoring) → paste a query from `kql-queries.md` → Run.
- Safe to share: the query text, the resource name. **Secret**: nothing in the query itself is secret, but review any *result rows* before sharing — a slow request's URL or custom dimensions could incidentally contain something sensitive depending on what the app logs (this app doesn't log anything sensitive per the re-audit above, but check real results anyway before sharing them).

**(e) Create the alert rule**

- Portal: the App Insights resource → Alerts → Create → Alert rule → Scope: this App Insights resource → Condition: "Server response time" (or a custom log metric on `POST /api/quotes` duration) → Aggregation: Average → Threshold: 500ms → Evaluation window: 5 minutes → Action group: create one with your email → Review + create.
- CLI (illustrative — metric alert rules are commonly created via `az monitor metrics alert create` referencing an action group created with `az monitor action-group create`):
  ```
  az monitor action-group create --name YOUR_ACTION_GROUP_NAME \
    --resource-group YOUR_RESOURCE_GROUP \
    --action email YOUR_EMAIL_RECEIVER_NAME your-email@example.com

  az monitor metrics alert create --name "slow-post-quotes" \
    --resource-group YOUR_RESOURCE_GROUP \
    --scopes YOUR_APP_INSIGHTS_RESOURCE_ID \
    --condition "avg requests/duration > 500" \
    --window-size 5m --evaluation-frequency 1m \
    --action YOUR_ACTION_GROUP_NAME
  ```
- Safe to share: the alert name, threshold, window. **Redact**: the resource IDs (contain the subscription ID) and the actual email address from any screenshot.

## What changed and where

All changes are in `day-3/task-3/QuotesApi`:

- `QuotesApi.csproj` — added `Azure.Monitor.OpenTelemetry.AspNetCore` (the real package name — the task text says `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore`, which doesn't exist on NuGet; confirmed by a 404), plus the two packages strictly required to implement "DefaultAzureCredential reading a secret from Key Vault": `Azure.Identity` and `Azure.Security.KeyVault.Secrets`.
- `Program.cs` — the exporter decision (below), Key Vault-backed connection string resolution, and the environment gate that keeps both entirely out of tests.

New test project `day-4/task-6/QuotesApi.Telemetry.Tests` (`ProjectReference` only, no Day 3 source duplicated).

## Package name corrections (verified, not assumed)

Two things in the task text don't match the real, published API, checked directly against NuGet and the compiled assembly rather than trusted from the task's wording:

- The package is **`Azure.Monitor.OpenTelemetry.AspNetCore`**, not `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` (404 on the literal name).
- The extension method is **`UseAzureMonitor()`**, not `AddAzureMonitor()` — confirmed by inspecting the compiled assembly's exported method names (`strings` on the DLL shows `UseAzureMonitor`/`UseAzureMonitorExporter`, not `AddAzureMonitor`) and by compiling a probe program against it.

## Version compatibility (checked, not assumed)

`Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` depends on `OpenTelemetry.Extensions.Hosting`/`Instrumentation.AspNetCore`/`Instrumentation.Http` **1.15.x**, while Task 5 already pinned all three to **1.17.0**. Ran the actual restore and `dotnet list package` afterward — all three resolved cleanly to `1.17.0` (the already-pinned, higher version), with no `NU1605` downgrade warning. Genuinely verified, not assumed compatible.

## The exporter decision

Task 5 configured `AddOtlpExporter()` for a local Jaeger/Aspire dashboard. Running that alongside Azure Monitor in every environment would mean every span exports twice, doubling overhead and requiring Key Vault to be reachable even for local development. Chosen instead: **OTLP stays a `Development`-only convenience; Azure Monitor is the exporter everywhere else** (any environment that is neither `"Testing"` nor `"Development"` — in practice, `"Production"` or any custom named deployment environment). Exactly one exporter is ever active per environment, never both, never neither (outside `Testing`, where correctly neither is active — see below).

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

- Never appears in `appsettings.json`, `appsettings.Development.json`, any committed file, any code literal, or any log line.
- Is fetched at startup via `Azure.Security.KeyVault.Secrets.SecretClient`, authenticated with `Azure.Identity.DefaultAzureCredential`.
- Is looked up under the fixed secret name **`ApplicationInsights-ConnectionString`** — this is the exact name the code expects; creating the secret under a different name means the app fails to start.
- Only the **vault name** is configuration (`"KeyVault:Name"`), used to build `https://{name}.vault.azure.net/`. That's not a secret, but it's still parameterized rather than baked into code.

`DefaultAzureCredential` tries a chain of credential sources in order; the two that matter here are: locally, it picks up your `az login` session (an interactive developer identity — this is exactly why the KeyVault fetch would work for me locally but never in CI, which has no such session); when deployed to Azure, it would instead pick up a **managed identity** assigned to the compute resource, with no credential material anywhere in configuration or code either way.

**Failure handling is explicit and deliberate.** If the vault is unreachable, `DefaultAzureCredential` can't authenticate, or the secret doesn't exist, `ResolveAppInsightsConnectionString` throws a generic `InvalidOperationException` — the *original* exception and its message are deliberately discarded rather than wrapped, so no Key Vault error detail (which could reference internal endpoints or partial state) ever reaches a log sink. The app does **not** start with telemetry silently disabled; a misconfigured, telemetry-blind production deployment that looks healthy is worse than a startup crash that's immediately obvious.

## Test isolation from Azure — how it's actually achieved

`ResolveAppInsightsConnectionString` (and therefore Key Vault, and therefore `UseAzureMonitor`) is only ever called when the environment is neither `"Testing"` nor `"Development"`. Every `WebApplicationFactory` in this repo (Tasks 2, 4, 5, and this one) calls `builder.UseEnvironment("Testing")`, so none of them ever reach that code path — no network call, no `DefaultAzureCredential` attempt, no dependency on a real subscription. Verified two ways:

1. Ran the entire existing 60-test suite (Tasks 2/4/5's tests) after this change — all still pass, in ~3.7 seconds wall-clock (no hang or retry against an unreachable endpoint).
2. Added a new, explicit regression test (`TestingEnvironment_StartsAndServesRequests_WithoutKeyVaultConfigured`) whose factory deliberately configures **no** `"KeyVault:Name"` at all. A successful `200 OK` response is itself the proof the Azure/Key Vault path was never reached — if the environment guard were ever removed or inverted, this test would fail immediately with a startup exception, not silently.

## Telemetry and PII re-audit

Once Azure Monitor is active, every log property and span tag genuinely leaves the machine for a cloud service, so everything from Tasks 4 and 5 was re-checked, not assumed still safe:

- Every `_logger.Log*` call in the codebase was grepped directly: the only structured properties are `UserId`, `FamilyId` (a GUID), and `LifetimeSeconds` — no password, token, refresh token, JWT, or `Authorization` header anywhere.
- Every `SetTag` call was grepped directly: `user.id` and `refresh_token.outcome` only — same conclusion.
- No `EnrichWithHttpRequest`/`EnrichWithHttpResponse` callback was added to `AddAspNetCoreInstrumentation()`/`AddHttpClientInstrumentation()`, so automatic instrumentation stays at the default semantic-convention tags (`http.route`, `http.request.method`, `http.response.status_code`) — it does not capture headers or request/response bodies.
- **Flagging, not concluding**: `user.id` is PII, and once Azure Monitor is wired up it is genuinely leaving this machine into a cloud service outside your direct control. That has real data-protection implications — retention period, the Azure region App Insights data is stored in, and who has access to query it — that deserve a real compliance review before this goes anywhere near production data. This is a flag for that review, not a compliance conclusion.

## KQL queries

See `kql-queries.md` for both queries with full explanations. Neither has been run against real telemetry yet — no App Insights resource exists to send telemetry to. Marked pending.

## The alert

The task's example targets `POST /api/quotes`, and — contrary to what the task text assumes — **that endpoint genuinely exists in this API** (confirmed by grepping `Program.cs` directly), so no substitution is needed. The alert would be configured for: average response time of `POST /api/quotes` exceeding 500ms over a 5-minute window, notifying by email.

The task's own principle — alerts should page only when something needs to be acted on, everything else is a dashboard — applies well here: a sustained latency regression on a write endpoint is something a human should actually look at, not noise. The one caveat: this specific `POST /api/quotes` handler does no I/O of its own (in-memory repository, no database, no downstream call), so in *this* codebase 500ms of average latency would be a very unusual, worth-investigating signal rather than routine variance — which is exactly the kind of alert that's worth having. Not yet created — pending an actual App Insights resource to attach it to.

## Teardown

Once resources exist, delete everything in one command by removing the resource group they were created in:

```
az group delete --name YOUR_RESOURCE_GROUP --yes --no-wait
```

`--no-wait` returns immediately; the group and everything inside it (App Insights, Log Analytics workspace, Key Vault, the alert rule) deletes asynchronously in the background. Nothing currently exists to tear down.

## What is safe to share vs. secret (recap)

- **Secret, never share**: the App Insights connection string (contains an ingestion key) — never in a file, commit, screenshot, URL, or log.
- **Redact from every file/example**: tenant ID, subscription ID, client/application ID → `YOUR_TENANT_ID`, `YOUR_SUBSCRIPTION_ID`, `YOUR_CLIENT_ID`.
- **Safe to share**: resource names, region, the KQL queries themselves, the Key Vault *name* (not its contents).

One more thing worth flagging plainly: `az account show` returned a tenant ID that is the *exact same value* already hardcoded as `"Entra:TenantId"` in `day-3/task-3/QuotesApi/appsettings.json` (committed in Day 3, not part of this task's changes). That looks like a real tenant ID sitting in a shared repo rather than a synthetic placeholder — not fixed here since it's out of this task's approved scope, but worth your own follow-up.
