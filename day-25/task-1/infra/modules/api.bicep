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
    // Required because this app has no system-assigned identity: Key Vault
    // references default to the system-assigned identity if this is unset,
    // and there isn't one here to fall back to - verified live via
    // Microsoft Learn's Key Vault references article on 2026-09-09 (see
    // ../README.md). Must be the identity's full ARM resource id, not its
    // client id or principal id.
    keyVaultReferenceIdentity: apiIdentityResourceId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: skuTier != 'Free'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output planName string = plan.name
