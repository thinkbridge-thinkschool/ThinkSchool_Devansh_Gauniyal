# Day 17 Task 1 — Deploy to Azure Static Web Apps

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-17/task-1/day-17/task-1

(Branch `day-17/task-1`, not `main`. Pending push at time of writing — see "Current status" below.)

## Notes for mentor

This replicates `day-16/task-2` at commit `83af57d8ff88d86d4e252a3cb88c3b4d4787ad22` and adds the Azure deployment; earlier folders are unchanged.

**Current status: local implementation and tests are complete and verified twice, independently. Azure deployment is blocked before Phase 5 completes, by a subscription-level Azure Policy, not by code, tests, or effort.** The subscription's system-managed "Allowed resource deployment regions" policy permits only `centralindia, austriaeast, uaenorth, koreacentral, malaysiawest`. Azure Static Web Apps deploys only to `centralus, eastus2, westus2, westeurope, eastasia`. Zero overlap. A scoped policy exemption and an owner-initiated parameter update were both attempted and both rejected by Azure itself (not just by a local safety check) — this account cannot lift the block. Microsoft support case `2608290030000568` asks only that `eastasia` be added to the allowed list; no billing, quota, or spending-limit change was requested. As of this submission the case is still open and the region list is unchanged (re-verified live, independently, in this session). Only one Azure object exists: the empty resource group `rg-thinkschool-d17-t1` — no billable resource has been created.

### 1. The brief

Verbatim from `brief.md`. The two bracketed URLs remain literal placeholders because no live deployment exists yet — filling them in would be fabricating evidence.

> Take the Angular 21 app from day-16/task-2 and copy it into day-17/task-1 untouched, then deploy it to Azure Static Web Apps on the free tier at [SWA URL]. Do not modify day-16 or anything earlier.
>
> The Week-1 API is the QuotesApi at day-3/task-3/QuotesApi. Copy it into day-17/task-1 as well and deploy that copy to Azure App Service on the free F1 tier at [API URL]. Do not modify the original under day-3.
>
> Its real endpoints: GET /api/quotes returns a list of quotes shaped { id: number, ownerId: string, text: string, author: string | null } and is anonymous. GET /api/protected and POST /api/quotes both require authorization. POST takes { text: string, author?: string }. The identifier field is id, an int. There are no validation attributes on any request DTO.
>
> The auth requirement is Managed Identity with no client secret anywhere. The API already has an Entra JWT scheme; point it at tenant YOUR_TENANT_ID with audience api://YOUR_CLIENT_ID. Create an Azure Function App with a system-assigned managed identity that acquires a token for that audience and calls one of the AUTHORIZED endpoints — not the anonymous list. The Angular app calls the Function, the Function calls the API.
>
> No secret may exist in the repository, in the Static Web App's settings, in the Function App's settings, or in the API's settings. No client secret, no connection string, no key, no stored token. If you cannot make something work without one, stop and say so rather than falling back to a secret.
>
> The Angular app currently hardcodes the relative path /api/quotes. Make the base URL configurable so it can point at the deployed Function, without breaking any of the 107 existing tests.
>
> Run Lighthouse against the live URL with Chrome and report the real scores, whatever they are.

### 2. The agent's output

`web/public/staticwebapp.config.json`:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": [
      "/favicon.ico",
      "/*.{css,js,map,png,jpg,jpeg,gif,svg,webp,ico,woff,woff2}"
    ]
  }
}
```

`.github/workflows/deploy-static-web-app.yml`:

```yaml
name: Deploy Day 17 Task 1 to Azure Static Web Apps

on:
  workflow_dispatch:
  push:
    branches:
      - day-17/task-1
    paths:
      - day-17/task-1/web/**
      - day-17/task-1/.github/workflows/deploy-static-web-app.yml

jobs:
  build_and_deploy:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: day-17/task-1/web/package-lock.json

      # Build in a normal shell instead of Oryx. This avoids Oryx's unquoted
      # source-path chown bug and makes the artifact uploaded below explicit.
      - name: Install and build Angular app
        working-directory: day-17/task-1/web
        run: |
          npm ci
          npm run build -- --define BUILD_API_BASE_URL="'${{ vars.DAY_17_FUNCTION_APP_BASE_URL }}'"

      - name: Deploy prebuilt Angular app
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_DAY_17_TASK_1 }}
          action: upload
          app_location: day-17/task-1/web/dist/app/browser
          api_location: ""
          output_location: ""
          skip_app_build: true
```

`function/ManagedIdentityQuotes.cs`:

```csharp
// This HTTP function acquires a token with the Function App's system-assigned managed
// identity, proves that token against the authorized GET /api/protected endpoint, and
// only then reads GET /api/quotes for the Angular UI. It deliberately proves access
// with /api/protected because the quotes list is anonymous and cannot demonstrate auth.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ManagedIdentityBridge;

public sealed class ManagedIdentityQuotes(
    TokenCredential credential,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ManagedIdentityQuotes> logger)
{
    private const string RequiredRole = "Api.Access";

    [Function("ManagedIdentityQuotes")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "quotes")]
        HttpRequestData request,
        FunctionContext context)
    {
        var apiBaseUrl = configuration["QuotesApiBaseUrl"]?.TrimEnd('/');
        var audience = configuration["EntraAudience"]?.TrimEnd('/');

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || apiBaseUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(audience))
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.InternalServerError,
                "bridge_configuration_invalid",
                "The Function App requires an HTTPS QuotesApiBaseUrl and an EntraAudience setting.");
        }

        try
        {
            var accessToken = await credential.GetTokenAsync(
                new TokenRequestContext([$"{audience}/.default"]),
                context.CancellationToken);

            var claims = ReadClaims(accessToken.Token);
            var audienceVerified = claims.Audiences.Contains(audience, StringComparer.Ordinal);
            var roleVerified = claims.Roles.Contains(RequiredRole, StringComparer.Ordinal);

            logger.LogInformation(
                "Managed identity token checked without logging its value. Audience matched: {AudienceVerified}; issuer present: {IssuerPresent}; required role present: {RoleVerified}.",
                audienceVerified,
                !string.IsNullOrWhiteSpace(claims.Issuer),
                roleVerified);

            if (!audienceVerified || !roleVerified)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.Forbidden,
                    "managed_identity_claims_invalid",
                    "The managed-identity token did not contain the configured audience and Api.Access role.");
            }

            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken.Token);

            using var protectedResponse = await client.GetAsync(
                new Uri(apiBaseUri, "/api/protected"),
                context.CancellationToken);

            if (!protectedResponse.IsSuccessStatusCode)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.BadGateway,
                    "authorized_endpoint_rejected_token",
                    $"GET /api/protected returned {(int)protectedResponse.StatusCode}.");
            }

            using var quotesResponse = await client.GetAsync(
                new Uri(apiBaseUri, "/api/quotes"),
                context.CancellationToken);

            if (!quotesResponse.IsSuccessStatusCode)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.BadGateway,
                    "quotes_endpoint_failed",
                    $"GET /api/quotes returned {(int)quotesResponse.StatusCode}.");
            }

            var response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.Headers.Add("X-Managed-Identity-Audience-Verified", "true");
            response.Headers.Add("X-Managed-Identity-Role-Verified", RequiredRole);
            response.Headers.Add("X-Managed-Identity-Authorized-Endpoint", "/api/protected");
            await response.WriteStringAsync(
                await quotesResponse.Content.ReadAsStringAsync(context.CancellationToken),
                context.CancellationToken);
            return response;
        }
        catch (CredentialUnavailableException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_unavailable",
                "The Function App could not access its system-assigned managed identity.");
        }
        catch (AuthenticationFailedException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_authentication_failed",
                "Azure rejected the Function App's managed-identity token request.");
        }
        catch (HttpRequestException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "quotes_api_unreachable",
                "The Function App could not reach the Quotes API over HTTPS.");
        }
        catch (InvalidOperationException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_token_invalid",
                "The managed-identity token could not be validated locally.");
        }
    }

    private static TokenClaims ReadClaims(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("The access token is not a JWT.");
        }

        var payload = segments[1]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = document.RootElement;
        var audiences = ReadStringOrArray(root, "aud");
        var roles = ReadStringOrArray(root, "roles");
        var issuer = root.TryGetProperty("iss", out var issuerElement)
            ? issuerElement.GetString()
            : null;

        return new TokenClaims(audiences, roles, issuer);
    }

    private static IReadOnlyList<string> ReadStringOrArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() is { } value ? [value] : [];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .OfType<string>()
                .ToArray();
        }

        return [];
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request,
        HttpStatusCode status,
        string code,
        string message)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(new { error = new { code, message } }),
            Encoding.UTF8);
        return response;
    }

    private sealed record TokenClaims(
        IReadOnlyList<string> Audiences,
        IReadOnlyList<string> Roles,
        string? Issuer);
}
```

### 3. The verification log

- **Live SWA URL / live API URL:** neither exists yet. Deployment is blocked before Phase 5 completes by the Azure Policy described above.
- **Lighthouse:** not run — there is no live URL to run it against. Chrome 152 is installed and ready; the moment a URL exists, `npx lighthouse <url> --output=json --output=html --chrome-flags="--headless"` is the only step remaining for this deliverable.
- **Managed-identity proof:** not yet possible — the Function has no deployed identity to test yet. Phase 6 (the manual `Api.Access` app-role assignment) cannot happen until Phase 5 creates the Function App, which cannot happen until the region is unblocked. The MI code path itself (`ManagedIdentityQuotes.cs` above) targets `GET /api/protected` specifically, not the anonymous `/api/quotes`, and is built to fail loudly and typed (`managed_identity_unavailable`, `managed_identity_claims_invalid`, `authorized_endpoint_rejected_token`, etc.) rather than silently.
- **Secret scan — confirmed clean, commands shown:**
  ```bash
  grep -rn "<real tenant GUID>" day-17/task-1 --exclude-dir={node_modules,bin,obj,.angular,dist}   # 0 hits
  grep -rn "<real client GUID>" day-17/task-1 --exclude-dir={node_modules,bin,obj,.angular,dist}    # 0 hits
  node day-17/task-1/scripts/verify-offline.mjs   # passes: SPA fallback, injectable API base URL, Oryx bypass, secret scan
  ```
  (The real GUID values are deliberately not reproduced even here, in a command shown to prove their absence — this document is itself a tracked file, and the rule is that they appear in none.)
  Neither the real tenant GUID nor the real client GUID appears in any tracked file, in any captured output, or in this document. Both appear only as `YOUR_TENANT_ID` / `YOUR_CLIENT_ID` placeholders in tracked files; the real values are supplied only as process arguments and Azure application settings at deployment time.
- **States exercised:** loading, error, and empty are already exercised by the 107 carried Angular tests (unchanged) plus the app's own carried empty/error/loading UI states. A 401-without-token state is proven at the API layer today: the copied `QuotesApi.Tests` suite (20/20 passing) includes authorization tests asserting `GET /api/protected` and `POST /api/quotes` reject an absent or invalid bearer token. The live 401-without-a-real-MI-token proof (Phase 7C) is not yet possible without a deployed Function.
- **The one concrete bug caught and fixed, with real error text:** this task's own offline verification script (`scripts/verify-offline.mjs`) was itself broken. Its "connection string" detector matched the bare words `Account` or `Key=` anywhere in a file — not the compound tokens a real Azure connection string actually uses. Running it produced:
  ```
  Offline verification failed (4):
  - README.md contains a possible connection string.
  - api/QuotesApi/Program.cs contains a possible connection string.
  - scripts/verify-offline.mjs contains a possible connection string.
  - verification-log.md contains a possible connection string.
  ```
  All four were false positives (the word "account" appearing in ordinary prose, and the script's own source containing its own pattern list). Fixed by requiring the actual compound key-value tokens a real Azure connection string uses (`DefaultEndpointsProtocol`, `AccountKey`, `SharedAccessSignature`, each followed by an equals sign) and excluding the script's own source from the scan. Proven with two real mutation checks: injecting a fake connection-string-shaped value produced a real, specific failure, then reverting produced a clean pass; breaking `staticwebapp.config.json`'s `navigationFallback.rewrite` produced a real failure naming exactly that field, then reverting produced a clean pass again. Full command output is in `verification-log.md`.
- **What breaks if the API's auth or a key endpoint changes:** if `Entra:Audience` or `Entra:TenantId` ever changes without updating the Function's `EntraAudience` setting, the `SmartBearer` issuer-routing scheme sends the resulting token to the wrong JWT handler and it fails with a 401 that looks identical to a bad password — nothing distinguishes "wrong audience" from "no token" from the outside. If `GET /api/protected`'s route or its `RequireAuthorization()` policy is ever removed, the Function's whole proof-of-auth path breaks silently, since that route is the only one the Function uses to demonstrate the token is real.

### Interpretations

- Replicated from `day-16/task-2` at commit `83af57d8ff88d86d4e252a3cb88c3b4d4787ad22`.
- Default `*.azurestaticapps.net` hostname; no custom domain, because [Azure documents that App Service Domains cannot be purchased on a credit-based/free-trial subscription without removing the spending limit](https://learn.microsoft.com/azure/app-service/manage-custom-dns-buy-domain) — the custom-domain *feature* itself is free on this tier, only the domain *name* was the blocker.
- A standalone Function, not a Static Web Apps linked backend, because [linking an existing Function to SWA requires the paid Standard plan](https://learn.microsoft.com/azure/static-web-apps/functions-bring-your-own); the trade-off is CORS instead of same-origin routing. [Static Web Apps' own managed identity is documented as being for Key Vault secret retrieval only](https://learn.microsoft.com/azure/static-web-apps/faq), not for calling a separate API.
- The managed-identity call targets `GET /api/protected`, not the anonymous `GET /api/quotes` — the anonymous route would "succeed" with or without a real token and prove nothing about authentication.
- The API's in-memory `Dictionary`-backed store does not survive an App Service restart; this is a demo-scale limitation, not something this task changes.
- No Azure SQL was provisioned despite the "Azure SQL + Managed Identity" topic tag — that tag names a topic, not a requirement; nothing in the exercise text asks for a database.
- The CI workflow lives inside `day-17/task-1/.github/workflows/`, not the repository-root `.github/workflows/`, so it cannot change repository-wide CI behavior outside this task's folder without a deliberate separate copy.
- Lighthouse: Chrome 152 is installed locally and ready to run; not yet executed because there is no live URL yet.

## What did you learn this session?

I assumed a free-tier resource was blocked only by its own tier limits, but a subscription can carry a completely separate region policy that vetoes it regardless of tier — and once that's a system-managed policy, no exemption or override request from my account can lift it, only Microsoft support can.

## What would break this?

The API's quotes live only in an in-memory dictionary, so a single App Service restart wipes everything anyone added — fine for this demo, useless the moment it needs to hold anything real. And if the audience or tenant ever drifts out of sync between the Function's setting and the API's Entra config, every call fails with a 401 that looks exactly like "no token," with nothing in the response to tell the two apart.
