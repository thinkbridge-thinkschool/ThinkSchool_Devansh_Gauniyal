# Day 17 Task 1 — Azure Static Web Apps and managed identity

This task carries the Day 16 Angular application and the Week-1 Quotes API into an isolated Day 17 folder, then adds a system-assigned managed-identity bridge for deployment to Azure. Earlier day folders remain unchanged.

## Architecture

```text
Browser
  -> Angular SPA on Azure Static Web Apps Free
  -> GET https://<function-app>.azurewebsites.net/api/quotes
  -> Azure Function on Consumption Y1, using its system-assigned identity
  -> bearer token for api://YOUR_CLIENT_ID
  -> authorized GET https://<api-app>.azurewebsites.net/api/protected
  -> anonymous GET https://<api-app>.azurewebsites.net/api/quotes
```

The browser cannot hold a managed identity because it runs on a user's device. The standalone Function is the Azure-owned resource that can obtain a token without a client secret. It first calls the genuinely protected `/api/protected` endpoint; only after that succeeds does it fetch the quote list needed by the SPA. Calling the anonymous list alone would not prove authentication.

The Function is called directly over CORS. It is not linked as a Static Web Apps backend because [linking an existing Function requires the Standard plan](https://learn.microsoft.com/azure/static-web-apps/functions-bring-your-own), which is outside the permitted Free tier. Microsoft also documents that [Static Web Apps' own managed identity is only for retrieving authentication secrets from Key Vault](https://learn.microsoft.com/azure/static-web-apps/faq); a separate Functions app is required for managed identity in this API path.

## Current deployment status

Everything is live, across two Azure subscriptions:

- API: `https://quotesapi-devansh-d17t1.azurewebsites.net` (App Service, F1, `centralindia`, original "Azure for Students" subscription)
- Function: `https://mi-bridge-devansh-d17t1.azurewebsites.net` (Consumption, system-assigned identity, `centralindia`, same subscription)
- Static Web App: `https://white-smoke-04fabcf10.7.azurestaticapps.net` (Free tier, `centralus`, a **second**, unrestricted Azure subscription)

The `Api.Access` app role was manually assigned to the Function's identity; a live call to the Function returns `200` with headers proving a real managed-identity token's audience and role were verified against the genuinely authorized `GET /api/protected` — see `verification-log.md` and `submission.md` for the full capture. The Static Web App calls that same Function over CORS, verified with a real preflight request.

**Why two subscriptions:** the original "Azure for Students" subscription carries a system-managed "Allowed resource deployment regions" policy (`centralindia, austriaeast, uaenorth, koreacentral, malaysiawest`), which has zero overlap with the five regions Static Web Apps supports (`centralus, eastus2, westus2, westeurope, eastasia`) — proven exhaustively, not assumed; a scoped exemption and an owner-initiated policy update were both directly rejected by Azure. A Microsoft support case (`2608290030000568`) asking for one region to be added was filed and remained unresolved. Devansh instead signed up for a second, genuinely unrestricted Azure subscription (a real Azure Free Account, confirmed as a first-time offer redemption). That subscription lives in a different Microsoft Entra tenant than `quotes-api`, and managed identity is tenant-bound, so the API and Function stayed exactly where they already worked; only the Static Web App — which needs no identity of its own — was created on the new subscription.

## Configuration and security

Tracked files use only these placeholders:

- tenant: `YOUR_TENANT_ID`
- client/application: `YOUR_CLIENT_ID`
- audience: `api://YOUR_CLIENT_ID`

Real identifiers are supplied only from the deployment process to Azure application settings. They are never printed, written to a file, or committed. The API's internal JWT scheme still validates at startup, so deployment generates its random signing key in process memory and writes it directly to the API application setting. The value is never displayed or saved locally.

The Functions storage binding must be converted to identity-based configuration (`AzureWebJobsStorage__accountName`) and the Function identity granted the minimum required data-plane roles; the storage connection string created by the provisioning command must then be removed. The Function's business API path contains no connection string, client secret, access key, or stored token.

The copied API accepts both tenant-specific Microsoft Entra access-token issuer formats while validating the same audience, signing keys, and lifetime:

- v2: `https://login.microsoftonline.com/{tenant}/v2.0`
- v1: `https://sts.windows.net/{tenant}/`

This is necessary because the target application registration currently uses the default v1 access-token format. Microsoft documents [both issuer formats](https://learn.microsoft.com/troubleshoot/entra/entra-id/app-integration/troubleshooting-signature-validation-errors) and that [the resource controls the access-token format](https://learn.microsoft.com/entra/identity-platform/access-tokens).

## Local verification

```bash
cd day-17/task-1/web
npm ci
npm test -- --watch=false
npm run build -- --define BUILD_API_BASE_URL="'https://function.example.invalid'"

cd ../api
dotnet test Task3.slnx

cd ../function
dotnet build ManagedIdentityBridge.csproj

cd ..
node scripts/verify-offline.mjs
```

The workflow builds Angular itself with `npm ci` and `npm run build`, then uploads `web/dist/app/browser` with `skip_app_build: true`. This bypasses Oryx's unquoted source-path handling while keeping the deployment token in the GitHub secret `AZURE_STATIC_WEB_APPS_API_TOKEN_DAY_17_TASK_1`. The deployed Function origin belongs in the repository variable `DAY_17_FUNCTION_APP_BASE_URL`. To activate the nested artifact later, copy it deliberately to the repository-root `.github/workflows` folder in a separate change; it is intentionally inactive here.

## Azure deployment boundary

Resources actually created, in `centralindia` (the four resource types available on this subscription's allowed-region list):

```text
resource group:    rg-thinkschool-d17-t1
App Service plan:  plan-day17-t1-api (Linux, F1/free)
Web App (API):     quotesapi-devansh-d17t1
Storage account:   stday17task1devansh (Standard_LRS)
Function App:      mi-bridge-devansh-d17t1 (Windows Consumption/Y1 — Linux Consumption
                    was unavailable in this region: `Linux dynamic workers are not
                    available in resource group .`; Windows Consumption succeeded)
```

Deployment commands obtained the real tenant and client identifiers from the process environment, applied them only as Azure application settings, and never wrote or displayed them. API settings: `Entra__TenantId`, `Entra__Audience`, and a freshly `openssl rand`-generated `InternalJwt__SigningKeyBase64`. Function settings: `QuotesApiBaseUrl`, `EntraAudience`, and `AzureWebJobsStorage__accountName` (identity-based, after removing the connection string `az functionapp create` added by default).

Static Web Apps still cannot be created on this subscription — see "Current deployment status" above for why. It was instead created on a second, unrestricted subscription:

```text
resource group:    rg-day17-task1-swa (second Azure subscription, different Entra tenant)
Static Web App:    swa-day17-task1-devansh (Free tier, centralus)
```

CORS on the Function App (original subscription) was updated to allow exactly this Static Web App's origin, verified with a real preflight request — see `verification-log.md`.

## Custom domain decision

The deployment uses the default `*.azurestaticapps.net` hostname. Static Web Apps Free supports custom-domain mappings, but a domain name is not available here: Microsoft states that [App Service domains are unsupported on free-trial or credit-based subscriptions and require removing the spending limit](https://learn.microsoft.com/azure/app-service/manage-custom-dns-buy-domain). No domain is purchased and the spending limit stays on.

## Teardown

Delete each real workload explicitly, then its resource group. Two subscriptions are involved — switch with `az account set --subscription <id>` between blocks, or pass `--subscription` on each command.

Original subscription (API, Function, Storage):
```bash
az functionapp delete --name mi-bridge-devansh-d17t1 --resource-group rg-thinkschool-d17-t1
az storage account delete --name stday17task1devansh --resource-group rg-thinkschool-d17-t1 --yes
az webapp delete --name quotesapi-devansh-d17t1 --resource-group rg-thinkschool-d17-t1
az appservice plan delete --name plan-day17-t1-api --resource-group rg-thinkschool-d17-t1 --yes
az group delete --name rg-thinkschool-d17-t1 --yes --no-wait
```

Second subscription (Static Web App):
```bash
az staticwebapp delete --name swa-day17-task1-devansh --resource-group rg-day17-task1-swa --yes
az group delete --name rg-day17-task1-swa --yes --no-wait
```

The existing `quotes-api` app registration is not created by this task and must not be deleted during teardown. Neither is the `Api.Access` app-role assignment on it — removing it (rather than deleting the whole registration) is the correct, narrower cleanup step if the Function identity's access should be revoked without touching the registration itself:

```bash
az rest --method DELETE \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/<QUOTES_API_SERVICE_PRINCIPAL_ID>/appRoleAssignments/<assignment id from the Graph response when it was created>"
```
