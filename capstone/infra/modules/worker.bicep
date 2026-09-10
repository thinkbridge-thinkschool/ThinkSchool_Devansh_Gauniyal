// Day 26 - the trace-demo worker's host: a second Linux Web App on the SAME App
// Service plan as modules/api.bicep's Web App, reusing the SAME user-assigned
// identity. See ../README.md, "Day 26 - the trace-demo worker" for the full
// reasoning and the WebJobs attempt that came before this and genuinely failed
// (Linux Kudu refused to run a continuous WebJob - see VERIFICATION-LOG.md).
//
// Why a second Web App on the same plan, not a new plan: an App Service plan is
// billed for its own compute (instance-hours x SKU), not per app hosted on it - a
// Basic B1 plan can host multiple Web Apps at no extra charge as long as the
// combined load fits in one instance's resources, which a low-volume demo worker
// trivially does. This is the same "no new resource type, no new cost" reasoning
// this project already applied when it chose to reuse the API's identity rather
// than mint a second one (see modules/servicebus.bicep's Day 26 comment).
//
// API version (Microsoft.Web/sites, serverFarms: 2026-07-15) is the same one
// modules/api.bicep already verified live on 2026-09-07 - this module targets an
// EXISTING plan (passed in as a parameter), not a new one, so there is no new
// resource type here to separately verify.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Resource id of the EXISTING App Service plan (from modules/api.bicep) this worker is hosted on - not a new plan.')
param appServicePlanId string

@description('Resource id of the API\'s user-assigned managed identity - reused as-is, not a second identity. See header comment.')
param apiIdentityResourceId string

@description('Client id of that same identity.')
param apiIdentityClientId string

@description('SQL server FQDN, from modules/sql.bicep.')
param sqlServerFqdn string

@description('SQL database name, from modules/sql.bicep.')
param sqlDatabaseName string

@description('Service Bus namespace FQDN, from modules/servicebus.bicep.')
param serviceBusNamespaceFqdn string

@description('The trace-demo subscription\'s topic and subscription names, from modules/servicebus.bicep.')
param demoSubscriptionTopicName string
param demoSubscriptionName string

@description('Key Vault URI and the App Insights connection string secret name, from modules/keyvault.bicep.')
param keyVaultUri string
param appInsightsConnectionStringSecretName string

@description('OpenTelemetry trace sampling ratio - same value the API uses, see modules/api.bicep.')
param otelSamplingRatio string = '1.0'

@description('Tags applied to every resource in this module.')
param tags object

var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'worker'), 0, 6)
var webAppName = 'capstone-${environmentName}-worker-${uniqueSuffix}'

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
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${appInsightsConnectionStringSecretName})'
  }
  {
    // Set out-of-band via `az webapp config appsettings set` during this session's
    // deploy (the worker is a pre-built, framework-dependent publish output, not
    // buildable source - see ../README.md's "Commands" for the equivalent on the
    // API). Declared here too so the next `az stack sub create` doesn't drift this
    // away - see main.bicep's Deployment Stacks section for why an undeclared
    // out-of-band app setting would otherwise be silently at risk on redeploy.
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
    serverFarmId: appServicePlanId
    httpsOnly: true
    keyVaultReferenceIdentity: apiIdentityResourceId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
