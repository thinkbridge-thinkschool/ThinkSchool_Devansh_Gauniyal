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

Local implementation and tests are complete. Azure deployment is paused in Phase 5 because the Azure for Students system policy allows only Central India, Austria East, UAE North, Korea Central, and Malaysia West, while Static Web Apps is offered only in Central US, East US 2, West US 2, West Europe, and East Asia. The lists do not overlap.

Microsoft support case `2608290030000568` asks only for East Asia to be added to the system-managed region list. No paid SKU, quota increase, spending-limit change, or billing change was requested. The only current Azure object is the empty resource group `rg-thinkschool-d17-t1` in Central India; no workload or billable resource exists.

After East Asia is enabled, deployment resumes there and must stop again after creating the Function identity, before the manual `Api.Access` app-role assignment required by Phase 6.

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

When support enables East Asia, create only the approved resources and confirm each SKU before executing its create command:

```text
resource group:  rg-thinkschool-d17-t1
App Service:     Linux F1 plan plus one Web App
Functions:       Consumption Y1 plus one system-assigned identity
Storage:         Standard_LRS
Static Web Apps: Free
region:          eastasia
```

Deployment commands must obtain the real tenant and client identifiers from the current process environment and redact them in displayed output. API settings are `Entra__TenantId`, `Entra__Audience`, and the process-generated `InternalJwt__SigningKeyBase64`. Function settings are `QuotesApiBaseUrl`, `EntraAudience`, and identity-based host-storage settings. Static Web Apps receives no application secret.

After the Function identity exists, stop before assigning `Api.Access`. Devansh must perform that directory-level app-role assignment manually and confirm it before any Phase 7 proof or Lighthouse run.

## Custom domain decision

The deployment uses the default `*.azurestaticapps.net` hostname. Static Web Apps Free supports custom-domain mappings, but a domain name is not available here: Microsoft states that [App Service domains are unsupported on free-trial or credit-based subscriptions and require removing the spending limit](https://learn.microsoft.com/azure/app-service/manage-custom-dns-buy-domain). No domain is purchased and the spending limit stays on.

## Teardown

No workload currently exists. To remove the empty resource group now:

```bash
az group delete --name rg-thinkschool-d17-t1 --yes --no-wait
```

After a successful deployment, delete each workload explicitly before deleting the group. Replace only the name placeholders with the names reported by deployment:

```bash
az staticwebapp delete --name <STATIC_WEB_APP_NAME> --resource-group rg-thinkschool-d17-t1 --yes
az functionapp delete --name <FUNCTION_APP_NAME> --resource-group rg-thinkschool-d17-t1
az storage account delete --name <STORAGE_ACCOUNT_NAME> --resource-group rg-thinkschool-d17-t1 --yes
az webapp delete --name <API_WEB_APP_NAME> --resource-group rg-thinkschool-d17-t1
az appservice plan delete --name <F1_PLAN_NAME> --resource-group rg-thinkschool-d17-t1 --yes
az group delete --name rg-thinkschool-d17-t1 --yes --no-wait
```

The existing `quotes-api` app registration is not created by this task and must not be deleted during teardown.
