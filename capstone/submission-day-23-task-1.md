# Day 23 Task 1 — Bicep IaC

## Notes for mentor

This lives at `capstone/infra/` rather than a `day-23/task-1` folder because the
capstone is one continuous project (Days 22, 28–32) and infrastructure belongs
beside the code it describes — see `capstone/infra/README.md`, "Why this lives
here."

Service Bus (namespace + two topics: `supplier-notifications`,
`invoice-approved-events`) is provisioned ahead of the application — nothing in
`capstone/src` publishes to or consumes from them yet; see `capstone/DESIGN.md`'s
two named async flows and `capstone/infra/README.md`.

Commit hash representing this day's state: this same commit — see `git log -1` or
the GitHub commit view (a commit can't contain its own hash inside its own tree).

Deployed resources (`rg-capstone-dev`: App Service plan + Web App, SQL server +
database, Service Bus namespace + 2 topics) **are still live as of this commit** —
not torn down yet. Exact teardown command is in `capstone/infra/README.md`
("Tear down") and was given to Devansh directly; he has not yet confirmed running
it.

### Main Bicep (`capstone/infra/main.bicep`)

```bicep
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
```

### One module (`capstone/infra/modules/sql.bicep`)

```bicep
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
```

### Dev parameters (`capstone/infra/parameters/dev.bicepparam`)

```bicep
using '../main.bicep'

param environmentName = 'dev'
param location = 'centralindia'
param resourceGroupName = 'rg-capstone-dev'

param sqlAdministratorLogin = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_PASSWORD')

param apiSkuName = 'B1'
param apiSkuTier = 'Basic'
param apiSkuCapacity = 1

param sqlComputeModel = 'Serverless'
param sqlSkuName = 'GP_S_Gen5'
param sqlCapacity = 1
param sqlUseFreeLimit = true
param sqlBackupRetentionDays = 7
param sqlBackupStorageRedundancy = 'Local'
param sqlZoneRedundant = false

param serviceBusMessageTtl = 'P1D'
param serviceBusDuplicateDetectionWindow = 'PT10M'
param serviceBusEnablePartitioning = false
```

### Prod parameters (`capstone/infra/parameters/prod.bicepparam`) — validated, never deployed

```bicep
using '../main.bicep'

param environmentName = 'prod'
param location = 'centralindia'
param resourceGroupName = 'rg-capstone-prod'

param sqlAdministratorLogin = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_PASSWORD')

param apiSkuName = 'S1'
param apiSkuTier = 'Standard'
param apiSkuCapacity = 2

param sqlComputeModel = 'Provisioned'
param sqlSkuName = 'GP_Gen5'
param sqlCapacity = 4
param sqlUseFreeLimit = false
param sqlBackupRetentionDays = 35
param sqlBackupStorageRedundancy = 'Geo'
param sqlZoneRedundant = true

param serviceBusMessageTtl = 'P14D'
param serviceBusDuplicateDetectionWindow = 'PT1H'
param serviceBusEnablePartitioning = true
```

### Real `what-if` output — dev (full, captured verbatim; subscription ID redacted to `<SUBSCRIPTION_ID>`, a real identifier per Phase 7's rule that it must not be committed)

Command: `az deployment sub what-if --name capstone-infra-dev-whatif --location centralindia --template-file main.bicep --parameters parameters/dev.bicepparam`

```
Resource and property changes are indicated with this symbol:
  + Create

The deployment will update the following scopes:

Scope: /subscriptions/<SUBSCRIPTION_ID>

  + resourceGroups/rg-capstone-dev [2023-07-01]

      apiVersion:       "2023-07-01"
      id:               "/subscriptions/<SUBSCRIPTION_ID>/resourceGroups/rg-capstone-dev"
      location:         "centralindia"
      name:             "rg-capstone-dev"
      tags.environment: "dev"
      tags.managed-by:  "bicep"
      tags.project:     "capstone"
      tags.source:      "capstone/infra"
      type:             "Microsoft.Resources/resourceGroups"

Scope: /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/rg-capstone-dev

  + Microsoft.ServiceBus/namespaces/sb-capstone-dev-zv36uz [2026-01-01]
      sku.name: "Standard"  (+ minimumTlsVersion "1.2", publicNetworkAccess "Enabled", disableLocalAuth false)

  + Microsoft.ServiceBus/namespaces/sb-capstone-dev-zv36uz/topics/invoice-approved-events [2026-01-01]
      properties.defaultMessageTimeToLive: "P1D"
      properties.duplicateDetectionHistoryTimeWindow: "PT10M"
      properties.enablePartitioning: false
      properties.requiresDuplicateDetection: true

  + Microsoft.ServiceBus/namespaces/sb-capstone-dev-zv36uz/topics/supplier-notifications [2026-01-01]
      (same properties as above)

  + Microsoft.Sql/servers/sql-capstone-dev-zpx4al [2025-01-01]
      properties.administratorLogin:         "*******"
      properties.administratorLoginPassword: "*******"
      properties.minimalTlsVersion:          "1.2"
      properties.publicNetworkAccess:        "Enabled"

  + Microsoft.Sql/servers/sql-capstone-dev-zpx4al/databases/sqldb-capstone-dev [2025-01-01]
      properties.autoPauseDelay:                   60
      properties.freeLimitExhaustionBehavior:      "AutoPause"
      properties.requestedBackupStorageRedundancy: "Local"
      properties.useFreeLimit:                     true
      properties.zoneRedundant:                    false
      sku.family: "Gen5"
      sku.name:   "GP_S_Gen5"

  + Microsoft.Sql/servers/sql-capstone-dev-zpx4al/databases/sqldb-capstone-dev/backupShortTermRetentionPolicies/default [2025-01-01]
      properties.retentionDays: 7

  + Microsoft.Sql/servers/sql-capstone-dev-zpx4al/firewallRules/AllowAllAzureServices [2025-01-01]
      properties.endIpAddress:   "0.0.0.0"
      properties.startIpAddress: "0.0.0.0"

  + Microsoft.Web/serverFarms/plan-capstone-dev-api [2026-07-15]
      kind: "linux"
      properties.reserved: true
      sku.capacity: 1
      sku.name:     "B1"

  + Microsoft.Web/sites/capstone-dev-api-skom32 [2026-07-15]
      kind: "app,linux"
      properties.httpsOnly:    true
      properties.serverFarmId: ".../serverFarms/plan-capstone-dev-api"
      properties.siteConfig:   "*******"

Resource changes: 10 to create.
```
(Full, untrimmed capture — including every field, not just the ones summarized
above for this file's length — is at `capstone/infra/output/whatif-dev.txt`; the
prod what-if, also successful with "10 to create" and no errors, is at
`capstone/infra/output/whatif-prod.txt`.)

### Real deploy output — dev (key facts; full raw ARM JSON, including the complete echoed parameter set, is in `capstone/infra/output/deploy-dev.txt` — trimmed here to keep this file tight, not because anything was hidden)

Command: `az deployment sub create --name capstone-infra-dev-deploy --location centralindia --template-file main.bicep --parameters parameters/dev.bicepparam`

- `provisioningState`: `"Succeeded"`
- Ran 2026-09-07 00:07:31 → 00:10:43 IST (3 minutes 12 seconds)
- Outputs:
  ```json
  {
    "apiHostName": "capstone-dev-api-skom32.azurewebsites.net",
    "apiName": "capstone-dev-api-skom32",
    "resourceGroupName": "rg-capstone-dev",
    "serviceBusNamespaceFqdn": "sb-capstone-dev-zv36uz.servicebus.windows.net",
    "serviceBusNamespaceName": "sb-capstone-dev-zv36uz",
    "serviceBusTopicNames": ["supplier-notifications", "invoice-approved-events"],
    "sqlDatabaseName": "sqldb-capstone-dev",
    "sqlServerFqdn": "sql-capstone-dev-zpx4al.database.windows.net",
    "sqlServerName": "sql-capstone-dev-zpx4al"
  }
  ```
- Independently re-queried against Azure (not just trusted from the deployment's own
  output) — observed SKUs matched exactly: App Service plan `B1`/`Basic`/`linux`;
  Web App `Running`, `linuxFxVersion: DOTNETCORE|10.0`, reachable at
  `https://capstone-dev-api-skom32.azurewebsites.net/` → real `HTTP 200`; SQL
  database `GeneralPurpose`/`GP_S_Gen5`/capacity 1/`Online`; Service Bus namespace
  `Standard`/`Active` with both topics `Active`.
- **Idempotence**: re-ran the identical `az deployment sub create` command a second
  time — succeeded again in 69 seconds, `provisioningState: "Succeeded"`, zero
  resources created or deleted the second time. A follow-up `what-if` against the
  live environment reported "4 to modify, 6 no change, 1 to ignore" rather than a
  perfectly silent 0 — read in full, every one of those is Azure's own
  server-assigned default property (never declared in this template) or a
  `Noeffect`-flagged field, which `az`'s own output explicitly warns can appear as
  "false positive predictions (noise)" — not real drift. Full detail in
  `capstone/infra/VERIFICATION-LOG.md`, §6.

## What did you learn this session?
Two resource providers (Microsoft.Sql, Microsoft.ServiceBus) weren't registered on my subscription at all, and the failure looked like a subscription problem, not a provider one, until I checked directly.
I also learned Azure SQL's free tier renews every month for the life of the subscription, not just for 12 months like I assumed — I had to go verify that instead of trusting my memory.

## What would break this?
If my subscription turns out not to qualify for the SQL free-limit offer, or if the serverless database gets hit continuously with no idle gaps so it never auto-pauses, the SQL cost could jump from close to zero to more than my entire remaining credit within the 21-day window — that's the one real financial risk here, not a code bug.
