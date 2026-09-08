using '../main.bicep'

// Prod: NOT deployed by this task - what-if validated only, per the hard rule that
// this task may only ever deploy dev. Kept here, and kept genuinely valid, so a
// later Academy day can deploy it deliberately rather than write it from scratch.
// See ../README.md, "How dev and prod differ" for the reasoning behind each value.

param environmentName = 'prod'
param location = 'centralindia'
param resourceGroupName = 'rg-capstone-prod'

param sqlAdministratorLogin = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_PASSWORD')

// API host: Standard S1, two instances - supports autoscale and deployment slots
// (neither configured here, but available), no daily quota, and enough headroom for
// more than a smoke test's worth of traffic.
param apiSkuName = 'S1'
param apiSkuTier = 'Standard'
param apiSkuCapacity = 2

// SQL: General Purpose Provisioned, 4 vCores (the smallest provisioned Gen5 size
// Central India actually offers - confirmed live via `az sql db list-editions` during
// Phase 3; provisioned tiers below 4 vCores don't exist), not serverless - a
// production database shouldn't auto-pause. No free limit (that offer is one
// database per subscription and dev already claims it). Zone-redundant, geo-
// replicated backups, and the maximum General Purpose PITR retention window.
param sqlComputeModel = 'Provisioned'
param sqlSkuName = 'GP_Gen5'
param sqlCapacity = 4
param sqlUseFreeLimit = false
param sqlBackupRetentionDays = 35
param sqlBackupStorageRedundancy = 'Geo'
param sqlZoneRedundant = true

// Service Bus: longer message TTL and duplicate-detection window, and partitioning
// on for throughput headroom across brokers. Tier is still Standard, not Premium -
// see modules/servicebus.bicep's header comment for why a namespace nothing calls
// yet doesn't justify Premium's ~88x base cost, in either environment.
param serviceBusMessageTtl = 'P14D'
param serviceBusDuplicateDetectionWindow = 'PT1H'
param serviceBusEnablePartitioning = true
