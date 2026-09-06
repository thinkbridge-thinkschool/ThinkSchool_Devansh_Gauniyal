// The SQL module: a logical server plus one database, sized to hold the Invoice and
// PurchaseOrder aggregates capstone/DESIGN.md describes. Provisioned ahead of the
// application's persistence code (see capstone/README.md, "what's deliberately not
// built yet" - both repositories are in-memory today).
//
// API versions (Microsoft.Sql/servers, servers/databases,
// servers/databases/backupShortTermRetentionPolicies: all 2025-01-01; firewallRules
// carried at the same version since it isn't separately enumerated by this
// subscription's provider metadata - see ../README.md) verified live via
// `az provider show -n Microsoft.Sql` on 2026-09-07. SKU names (GP_S_Gen5 serverless,
// GP_Gen5 provisioned) verified live via `az sql db list-editions -l centralindia`
// on the same date, after registering the Microsoft.Sql resource provider (it was
// NotRegistered on this subscription until this session).

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('SQL logical server administrator login. No default - supplied by the caller.')
param administratorLogin string

@secure()
@description('SQL logical server administrator password. No default - supplied by the caller. Never logged or output.')
param administratorPassword string

@description('Compute model: Serverless (dev) auto-pauses and can use the free monthly limit; Provisioned (prod) runs continuously at a fixed vCore count.')
@allowed([
  'Serverless'
  'Provisioned'
])
param computeModel string

@description('sku.name - GP_S_Gen5 for serverless General Purpose Gen5, GP_Gen5 for provisioned General Purpose Gen5.')
param skuName string

@description('vCore capacity.')
param capacity int

@description('Whether to apply the Azure SQL Database free monthly limit (100,000 vCore-seconds + 32 GB/month, for the lifetime of the subscription - one database per subscription). Dev only; ignored when computeModel is Provisioned.')
param useFreeLimit bool = false

@description('Point-in-time restore backup retention, in days (1-35 for General Purpose).')
param backupRetentionDays int = 7

@description('Backup storage redundancy: Local (dev) or Geo (prod).')
@allowed([
  'Local'
  'Zone'
  'Geo'
  'GeoZone'
])
param backupStorageRedundancy string = 'Local'

@description('Whether the database is zone redundant.')
param zoneRedundant bool = false

@description('Tags applied to every resource in this module.')
param tags object

// SQL server names are globally unique across all of Azure, so - same reasoning as
// the API module - the suffix is deterministic per subscription/environment rather
// than random, so re-running this deployment targets the same server instead of
// creating a new one each time.
var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'sql'), 0, 6)
var serverName = 'sql-capstone-${environmentName}-${uniqueSuffix}'
var databaseName = 'sqldb-capstone-${environmentName}'

// Serverless-only properties. Applied via union() below only when computeModel is
// Serverless - a Provisioned database rejects autoPauseDelay/minCapacity/free-limit
// properties outright, so they must not be present on that request at all, not just
// left at a default.
var serverlessProperties = {
  autoPauseDelay: 60
  minCapacity: json('0.5')
  useFreeLimit: useFreeLimit
  freeLimitExhaustionBehavior: useFreeLimit ? 'AutoPause' : 'BillForUsage'
}

resource sqlServer 'Microsoft.Sql/servers@2025-01-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Coarse-grained on purpose for this scaffold: lets any Azure-hosted resource
// (specifically this module's own Web App, which has no static outbound IP without
// VNet integration) reach the server. See ../README.md, "honest limits" - a
// production version needs private endpoints/VNet integration instead of this rule.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sqlServer
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: capacity
  }
  properties: union(
    {
      zoneRedundant: zoneRedundant
      requestedBackupStorageRedundancy: backupStorageRedundancy
    },
    computeModel == 'Serverless' ? serverlessProperties : {}
  )
}

resource backupRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2025-01-01' = {
  parent: database
  name: 'default'
  properties: {
    retentionDays: backupRetentionDays
  }
}

output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
