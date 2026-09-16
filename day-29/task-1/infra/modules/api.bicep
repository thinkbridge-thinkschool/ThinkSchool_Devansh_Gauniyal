// The API host module: an App Service plan (Linux) plus one Linux Web App running
// the capstone's ASP.NET Core host (capstone/host/Capstone.Web). Deployed into the
// resource group main.bicep creates - never into an existing plan, and never into
// rg-thinkschool-d17-t1 (Day 17's plan-day17-t1-api is untouched by this module).
//
// API versions (Microsoft.Web/serverFarms, Microsoft.Web/sites: 2026-07-15) verified
// live via `az provider show -n Microsoft.Web` on 2026-09-07 - see ../README.md.
// Runtime stack (DOTNETCORE|10.0) verified via `az webapp list-runtimes --os linux`
// on the same date - it matches capstone/host/Capstone.Web's net10.0 target.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('App Service plan SKU name, e.g. B1 (dev) or S1 (prod).')
param skuName string

@description('App Service plan SKU tier, e.g. Basic (dev) or Standard (prod).')
param skuTier string

@description('Number of plan instances.')
param skuCapacity int = 1

@description('Resource id of the user-assigned managed identity to attach to this Web App - see modules/identity.bicep. Also set as keyVaultReferenceIdentity below, since this app has no system-assigned identity for Key Vault references to fall back to.')
param apiIdentityResourceId string

@description('Client id of the same user-assigned managed identity. Set as the AZURE_CLIENT_ID app setting so DefaultAzureCredential/ManagedIdentityCredential in the Service Bus SDK knows which of this identity to use (a Web App can have more than one user-assigned identity; there is no ambiguity today with only one attached, but the app setting is what the SDK actually reads, not an assumption based on there being only one).')
param apiIdentityClientId string

@description('SQL server FQDN, from modules/sql.bicep - used to build the identity-based connection string below. No credential of any kind is embedded in it.')
param sqlServerFqdn string

@description('SQL database name, from modules/sql.bicep.')
param sqlDatabaseName string

@description('Service Bus namespace fully-qualified domain name, from modules/servicebus.bicep - a hostname, not a secret, used with DefaultAzureCredential by the Service Bus SDK rather than a connection string with an embedded key.')
param serviceBusNamespaceFqdn string

@description('Key Vault URI, from modules/keyvault.bicep - used to build the Key Vault reference app setting below.')
param keyVaultUri string

@description('Name of the demo secret in that vault, from modules/keyvault.bicep.')
param keyVaultDemoSecretName string

@description('Day 26: name of the Key Vault secret holding the Application Insights connection string, from modules/keyvault.bicep. Referenced the same way as keyVaultDemoSecretName - never a plaintext app setting. See ../README.md, "Day 26 - why the connection string still goes through Key Vault".')
param appInsightsConnectionStringSecretName string

@description('Day 26: Service Bus topic the trace-demo worker\'s subscription lives on, from modules/servicebus.bicep.')
param demoSubscriptionTopicName string

@description('Day 26: name of the trace-demo worker\'s Service Bus subscription, from modules/servicebus.bicep. Exposed as an app setting so the WebJob (host/Capstone.Worker, deployed into this same Web App - see ../README.md) knows what to read from without hardcoding it.')
param demoSubscriptionName string

@description('Day 26: OpenTelemetry trace sampling ratio, 0.0-1.0. Passed through as an app setting, read by both the API and the WebJob at startup - see ../README.md, "Day 26 - sampling" for why dev uses 1.0 (no sampling) rather than the platform default.')
param otelSamplingRatio string = '1.0'

@description('Day 27: resource id of the App Service VNet Integration subnet, from modules/network.bicep - required to reach the now-private-endpoint-only SQL server and Service Bus namespace. See ../README.md\'s Day 27 section.')
param appServiceIntegrationSubnetId string

@description('Day 27: the API\'s own Entra ID app registration id (see submission-day-27-task-1.md), used as the JWT bearer audience. Not a secret - an app id is a public identifier, the same way apiIdentityClientId above is.')
param apiAadAppId string

@description('Tags applied to every resource in this module.')
param tags object

// Web app names are globally unique across all of Azure (they get a
// *.azurewebsites.net hostname), so a per-environment suffix is derived
// deterministically from the subscription and environment - same inputs always
// produce the same name, which is what keeps a redeploy idempotent rather than
// creating a second app each time.
var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'api'), 0, 6)
var planName = 'plan-capstone-${environmentName}-api'
var webAppName = 'capstone-${environmentName}-api-${uniqueSuffix}'

resource plan 'Microsoft.Web/serverFarms@2026-07-15' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
    tier: skuTier
    capacity: skuCapacity
  }
  properties: {
    reserved: true
  }
}

// Day 25: identity-based connections for SQL and Service Bus, plus one Key
// Vault reference proving that mechanism, in place of any connection-string
// credential. See ../README.md, "What zero secrets actually looks like here"
// for why each of these four values is safe to read back in plain text (three
// of them) or resolves without ever exposing a plaintext secret (the fourth).
var appSettings = [
  {
    name: 'AZURE_CLIENT_ID'
    value: apiIdentityClientId
  }
  {
    name: 'ConnectionStrings__CapstoneDb'
    value: 'Server=${sqlServerFqdn};Authentication=Active Directory Managed Identity;Encrypt=True;User Id=${apiIdentityClientId};Database=${sqlDatabaseName}'
  }
  {
    name: 'ServiceBus__FullyQualifiedNamespace'
    value: serviceBusNamespaceFqdn
  }
  {
    name: 'DemoConfig__SampleSetting'
    value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${keyVaultDemoSecretName})'
  }
  {
    // Day 26: a Key Vault reference, not a plaintext connection string - see
    // ../README.md. The connection string still names the Application Insights
    // resource (Azure Monitor needs to know WHICH resource to talk to), but every
    // actual ingestion request is authenticated with the managed identity's
    // Entra ID token (options.Credential in Program.cs), not the string itself -
    // DisableLocalAuth: true on the component (modules/monitoring.bicep) makes
    // that the only path in.
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${appInsightsConnectionStringSecretName})'
  }
  {
    name: 'ServiceBus__DemoSubscriptionTopicName'
    value: demoSubscriptionTopicName
  }
  {
    name: 'ServiceBus__DemoSubscriptionName'
    value: demoSubscriptionName
  }
  {
    name: 'OTEL_SAMPLING_RATIO'
    value: otelSamplingRatio
  }
  {
    // Day 27: see host/Capstone.Web/Program.cs's comment on Auth:TenantId -
    // a tenant GUID, not a secret; it tells the JWT bearer handler which Entra
    // ID tenant issued a token, nothing a caller couldn't already infer.
    // subscription().tenantId, not a parameter - this deployment's own tenant is
    // always the right value, with no extra input to supply or keep in sync.
    name: 'Auth__TenantId'
    value: subscription().tenantId
  }
  {
    name: 'Auth__ApiAppId'
    value: apiAadAppId
  }
  {
    // Day 26: same reasoning as modules/worker.bicep's identical setting - set
    // out-of-band during this session's app-code deploy, declared here too so a
    // future redeploy doesn't drift it away.
    name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
    value: 'false'
  }
]

resource webApp 'Microsoft.Web/sites@2026-07-15' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Day 27: a real ZAP baseline scan flagged the platform's own ARRAffinity /
    // ARRAffinitySameSite cookies (SameSite=None) - see submission-day-27-task-1.md.
    // Client affinity (sticky sessions) is for stateful apps that need a client
    // pinned to one instance; this API has no per-instance state to pin to (both
    // repositories are in-memory per Program.cs, but nothing here depends on a
    // repeat request landing on the same instance), so disabling it removes the
    // cookies at the source instead of just hiding them.
    clientAffinityEnabled: false
    // Required because this app has no system-assigned identity: Key Vault
    // references default to the system-assigned identity if this is unset,
    // and there isn't one here to fall back to - verified live via
    // Microsoft Learn's Key Vault references article on 2026-09-09 (see
    // ../README.md). Must be the identity's full ARM resource id, not its
    // client id or principal id.
    keyVaultReferenceIdentity: apiIdentityResourceId
    // Day 27: regional VNet Integration - required now that SQL and Service Bus
    // are private-endpoint-only (modules/sql.bicep, modules/servicebus.bicep).
    // Without this, the app has no route to either private IP at all. Basic
    // (this plan's tier) supports regional VNet Integration - confirmed live
    // against Microsoft Learn's App Service networking features article on
    // 2026-09-11 (Free and Shared/Consumption do not).
    virtualNetworkSubnetId: appServiceIntegrationSubnetId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: skuTier != 'Free'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      // Routes ALL outbound traffic through the VNet integration, not just
      // RFC1918 destinations - without this, only traffic to the VNet's own
      // address space would use the integration path by default, which happens
      // to include the private endpoints here, but the explicit setting is what
      // this project's own Day 26 App Insights ingestion (a public Azure Monitor
      // endpoint) and the managed-identity token endpoint still need to keep
      // working un-routed through the VNet - verified live against Microsoft
      // Learn's VNet Integration routing article on 2026-09-11 that
      // vnetRouteAllEnabled: true, not the subnet id alone, is what governs this.
      vnetRouteAllEnabled: true
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output planName string = plan.name
output planId string = plan.id
