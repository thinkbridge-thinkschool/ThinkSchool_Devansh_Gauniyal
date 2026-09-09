// Key Vault for the API's remaining, genuinely-secret config. Honest note up
// front (see ../README.md for the full reasoning): once SQL and Service Bus
// both move to identity-based connections (this same day's other modules),
// there is no longer any real secret left for this scaffold's actual
// configuration - a connection string with "Authentication=Active Directory
// Managed Identity" and no credential, and a Service Bus namespace hostname,
// are both safe as plain app settings. This module and its one demo secret
// exist to wire and prove the Key Vault reference mechanism end-to-end - see
// DemoConfig--SampleSetting below - ready for the day a real secret (a
// third-party API key, for instance) actually needs one, matching this
// project's existing "infrastructure ahead of the application" pattern (see
// Service Bus in modules/servicebus.bicep).
//
// Access model: RBAC (enableRbacAuthorization: true), not vault access
// policies. Access policies are Key Vault's older, vault-local permission
// model; RBAC uses the same Azure role-assignment mechanism as every other
// resource in this project (see modules/servicebus.bicep's Data Sender role),
// so one consistent mental model covers both, and access shows up in the same
// `az role assignment list` a reviewer already knows to check.
//
// API version (Microsoft.KeyVault/vaults, vaults/secrets: 2026-05-15) and the
// Key Vault Secrets User role definition id (4633458b-17de-408a-b874-0445c86b69e6)
// verified live via `az provider show -n Microsoft.KeyVault` and
// `az role definition list --name "Key Vault Secrets User"` on 2026-09-09 -
// see ../README.md.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Principal id of the API\'s user-assigned managed identity - the only principal granted read access to secrets in this vault.')
param apiPrincipalId string

@secure()
@description('Value for the one demo secret (DemoConfig--SampleSetting) that proves the Key Vault reference mechanism resolves end-to-end. Not a real application secret - see the header comment above. No default - supplied at deploy time only, never committed.')
param demoSecretValue string

@description('Tags applied to every resource in this module.')
param tags object

var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'kv'), 0, 6)
var vaultName = 'kv-capstone-${environmentName}-${uniqueSuffix}'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource demoSecret 'Microsoft.KeyVault/vaults/secrets@2026-05-15' = {
  parent: vault
  name: 'DemoConfig--SampleSetting'
  properties: {
    value: demoSecretValue
  }
}

// Least privilege: Key Vault Secrets User can GET and LIST secrets - read
// only, no ability to set/delete a secret or manage the vault itself. Scoped
// to this one vault, not the whole resource group or subscription.
resource secretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, apiPrincipalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
output demoSecretName string = demoSecret.name
