// Capstone infrastructure - main deployment template.
//
// Subscription-scoped: this template creates its own resource group (never reusing
// rg-thinkschool-d17-t1 or DefaultResourceGroup-CID from Day 17) and deploys the API,
// SQL, and Service Bus modules into it in one deployment. See README.md for the
// dev/prod parameter differences and how to run this.
//
// API versions below were verified live against this subscription on 2026-09-07 via
// `az provider show`, not written from memory - see README.md's "API versions used"
// section for the exact commands and full results.
targetScope = 'subscription'

@description('Environment name. Controls SKU, capacity, retention, and redundancy defaults passed to every module - see the dev.bicepparam / prod.bicepparam files.')
@allowed([
  'dev'
  'prod'
])
param environmentName string

@description('Azure region for every resource this template creates.')
@allowed([
  'centralindia'
  'southindia'
  'westindia'
])
param location string = 'centralindia'

@description('Name of the resource group this deployment creates and deploys into. Must not be rg-thinkschool-d17-t1 or DefaultResourceGroup-CID - those belong to unrelated, already-deployed Day 17 infrastructure and this template never references them.')
param resourceGroupName string = 'rg-capstone-${environmentName}'

@description('SQL logical server administrator login name. Not a secret by itself, but has no default in the parameter files - see README.md.')
param sqlAdministratorLogin string

@secure()
@description('SQL logical server administrator password. No default here or in any parameter file - supplied at deploy time only (see README.md, "Supplying the SQL credential").')
param sqlAdministratorPassword string

@description('App Service plan SKU name, e.g. B1 (dev) or S1 (prod).')
param apiSkuName string

@description('App Service plan SKU tier, e.g. Basic (dev) or Standard (prod).')
param apiSkuTier string

@description('App Service plan instance count.')
param apiSkuCapacity int = 1

@description('SQL database compute model.')
@allowed([
  'Serverless'
  'Provisioned'
])
param sqlComputeModel string

@description('SQL database sku.name - e.g. GP_S_Gen5 (dev, serverless) or GP_Gen5 (prod, provisioned), or a DTU-based name like S0.')
param sqlSkuName string

@description('SQL database sku.tier. Defaults to GeneralPurpose for the vCore-based skuNames this template was authored for - see modules/sql.bicep.')
param sqlSkuTier string = 'GeneralPurpose'

@description('SQL database sku.family. Defaults to Gen5; pass an empty string alongside a DTU-based sqlSkuName - see modules/sql.bicep.')
param sqlSkuFamily string = 'Gen5'

@description('SQL database DTU or vCore capacity, matching sqlSkuName/sqlSkuTier.')
param sqlCapacity int

@description('Whether this database uses the Azure SQL Database free monthly limit (dev only - one free-limit database per subscription; see README.md).')
param sqlUseFreeLimit bool = false

@description('Point-in-time restore backup retention, in days (1-35 for General Purpose).')
param sqlBackupRetentionDays int

@description('Backup storage redundancy.')
@allowed([
  'Local'
  'Zone'
  'Geo'
  'GeoZone'
])
param sqlBackupStorageRedundancy string

@description('Whether the SQL database is zone redundant.')
param sqlZoneRedundant bool = false

@description('Object id of the Entra ID principal to set as the SQL server\'s Entra administrator - see modules/sql.bicep and README.md, "Manual step 1". No default - supplied at deploy time only.')
param entraAdminObjectId string

@description('Display name / UPN of that same Entra ID principal.')
param entraAdminLogin string

@secure()
@description('Value for the Key Vault demo secret proving the Key Vault reference mechanism resolves end-to-end - see modules/keyvault.bicep. Not a real application secret. No default - supplied at deploy time only, never committed.')
param keyVaultDemoSecretValue string

@description('Service Bus topic default message time-to-live, ISO 8601 duration.')
param serviceBusMessageTtl string

@description('Service Bus duplicate-detection history window, ISO 8601 duration.')
param serviceBusDuplicateDetectionWindow string

@description('Whether Service Bus topics use partitioning across message brokers.')
param serviceBusEnablePartitioning bool

@description('Day 26: Log Analytics data retention, in days - see modules/monitoring.bicep.')
param logAnalyticsRetentionDays int

@description('Day 26: daily ingestion cap, in GB, applied to both the Log Analytics workspace and the Application Insights component - see modules/monitoring.bicep and README.md.')
param dailyIngestionCapGb int

@description('Day 26: OpenTelemetry trace sampling ratio (0.0-1.0), passed to the API and worker as an app setting.')
param otelSamplingRatio string = '1.0'

@description('Day 26: notification email for the error-rate alert\'s action group. No default - supplied at deploy time only via CAPSTONE_ALERT_EMAIL, exactly like the SQL credential above, so a real address is never committed. See infra/README.md.')
param alertNotificationEmail string

@description('Day 26: error-rate percentage that triggers the alert.')
param alertErrorRateThresholdPercent int = 30

@description('Day 26: alert severity, 0 (critical) - 4 (verbose).')
param alertSeverity int = 2

@description('Common tags applied to every resource this template creates, so all of it is easy to find (and delete) later.')
param tags object = {
  project: 'capstone'
  'managed-by': 'bicep'
  environment: environmentName
  source: 'capstone/infra'
}

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Day 25: deployed first - sql, serviceBus, keyVault, and api all reference
// its outputs, so Bicep infers the dependency automatically without an
// explicit dependsOn.
module identity 'modules/identity.bicep' = {
  name: 'capstone-identity-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module api 'modules/api.bicep' = {
  name: 'capstone-api-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    skuName: apiSkuName
    skuTier: apiSkuTier
    skuCapacity: apiSkuCapacity
    apiIdentityResourceId: identity.outputs.resourceId
    apiIdentityClientId: identity.outputs.clientId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    serviceBusNamespaceFqdn: serviceBus.outputs.namespaceFqdn
    keyVaultUri: keyVault.outputs.vaultUri
    keyVaultDemoSecretName: keyVault.outputs.demoSecretName
    appInsightsConnectionStringSecretName: keyVault.outputs.appInsightsConnectionStringSecretName
    demoSubscriptionTopicName: serviceBus.outputs.demoSubscriptionTopicName
    demoSubscriptionName: serviceBus.outputs.demoSubscriptionName
    otelSamplingRatio: otelSamplingRatio
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'capstone-sql-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    administratorLogin: sqlAdministratorLogin
    administratorPassword: sqlAdministratorPassword
    computeModel: sqlComputeModel
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    skuFamily: sqlSkuFamily
    capacity: sqlCapacity
    useFreeLimit: sqlUseFreeLimit
    backupRetentionDays: sqlBackupRetentionDays
    backupStorageRedundancy: sqlBackupStorageRedundancy
    zoneRedundant: sqlZoneRedundant
    entraAdminObjectId: entraAdminObjectId
    entraAdminLogin: entraAdminLogin
    tags: tags
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'capstone-servicebus-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    messageTimeToLive: serviceBusMessageTtl
    duplicateDetectionWindow: serviceBusDuplicateDetectionWindow
    enablePartitioning: serviceBusEnablePartitioning
    apiPrincipalId: identity.outputs.principalId
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'capstone-keyvault-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    apiPrincipalId: identity.outputs.principalId
    demoSecretValue: keyVaultDemoSecretValue
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    tags: tags
  }
}

module worker 'modules/worker.bicep' = {
  name: 'capstone-worker-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    appServicePlanId: api.outputs.planId
    apiIdentityResourceId: identity.outputs.resourceId
    apiIdentityClientId: identity.outputs.clientId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    serviceBusNamespaceFqdn: serviceBus.outputs.namespaceFqdn
    demoSubscriptionTopicName: serviceBus.outputs.demoSubscriptionTopicName
    demoSubscriptionName: serviceBus.outputs.demoSubscriptionName
    keyVaultUri: keyVault.outputs.vaultUri
    appInsightsConnectionStringSecretName: keyVault.outputs.appInsightsConnectionStringSecretName
    otelSamplingRatio: otelSamplingRatio
    tags: tags
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'capstone-monitoring-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    logAnalyticsRetentionDays: logAnalyticsRetentionDays
    dailyIngestionCapGb: dailyIngestionCapGb
    apiPrincipalId: identity.outputs.principalId
    tags: tags
  }
}

module alerting 'modules/alerting.bicep' = {
  name: 'capstone-alerting-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    notificationEmail: alertNotificationEmail
    errorRateThresholdPercent: alertErrorRateThresholdPercent
    severity: alertSeverity
    tags: tags
  }
}

output resourceGroupName string = rg.name
output apiName string = api.outputs.webAppName
output apiHostName string = api.outputs.defaultHostName
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output serviceBusNamespaceFqdn string = serviceBus.outputs.namespaceFqdn
output serviceBusTopicNames array = serviceBus.outputs.topicNames
output identityName string = identity.outputs.name
output identityClientId string = identity.outputs.clientId
output identityPrincipalId string = identity.outputs.principalId
output keyVaultName string = keyVault.outputs.vaultName
output serviceBusDemoSubscriptionTopicName string = serviceBus.outputs.demoSubscriptionTopicName
output serviceBusDemoSubscriptionName string = serviceBus.outputs.demoSubscriptionName
output logAnalyticsWorkspaceName string = monitoring.outputs.logAnalyticsWorkspaceName
output appInsightsName string = monitoring.outputs.appInsightsName
output alertActionGroupName string = alerting.outputs.actionGroupName
output alertName string = alerting.outputs.alertName
output workerName string = worker.outputs.webAppName
output workerHostName string = worker.outputs.defaultHostName
