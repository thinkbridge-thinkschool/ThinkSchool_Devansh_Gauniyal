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

@description('SQL database sku.name - the family/tier string, e.g. GP_S_Gen5 (dev, serverless) or GP_Gen5 (prod, provisioned).')
param sqlSkuName string

@description('SQL database vCore capacity.')
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

@description('Service Bus topic default message time-to-live, ISO 8601 duration.')
param serviceBusMessageTtl string

@description('Service Bus duplicate-detection history window, ISO 8601 duration.')
param serviceBusDuplicateDetectionWindow string

@description('Whether Service Bus topics use partitioning across message brokers.')
param serviceBusEnablePartitioning bool

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

module api 'modules/api.bicep' = {
  name: 'capstone-api-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    skuName: apiSkuName
    skuTier: apiSkuTier
    skuCapacity: apiSkuCapacity
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
    capacity: sqlCapacity
    useFreeLimit: sqlUseFreeLimit
    backupRetentionDays: sqlBackupRetentionDays
    backupStorageRedundancy: sqlBackupStorageRedundancy
    zoneRedundant: sqlZoneRedundant
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
