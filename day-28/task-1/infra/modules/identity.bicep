// The API's managed identity. User-assigned, not system-assigned: this identity
// needs to exist and be grantable (Key Vault role, Service Bus role, the SQL
// database user) BEFORE the Web App that will use it is even created - Phase 3
// deploys this module, then the SQL/Service Bus grants can be prepared against a
// stable identity, independent of the Web App's own lifecycle. A system-assigned
// identity is created and destroyed with its Web App, which would mean re-doing
// every grant (including the manual SQL one a human must run - see
// ../README.md) on every future App Service recreation. One user-assigned
// identity, referenced by the Web App, survives that.
//
// API version verified live via `az provider show -n Microsoft.ManagedIdentity`
// on 2026-09-09 (latest non-preview: 2024-11-30) - see ../README.md.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Tags applied to this resource.')
param tags object

var identityName = 'id-capstone-${environmentName}-api'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
  tags: tags
}

output name string = identity.name
output resourceId string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
