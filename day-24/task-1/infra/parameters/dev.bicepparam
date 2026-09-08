using '../main.bicep'

// Dev: cheapest viable, predictable settings for a short-lived validation/demo
// deployment. See ../../README.md, "How dev and prod differ" for the reasoning
// behind each value below, and "Supplying the SQL credential" for what
// CAPSTONE_SQL_ADMIN_LOGIN / CAPSTONE_SQL_ADMIN_PASSWORD must be set to before
// running this.

param environmentName = 'dev'
param location = 'centralindia'
param resourceGroupName = 'rg-capstone-dev'

// No default anywhere in this file or main.bicep - both come from the environment
// at deploy time, never committed. See ../README.md.
param sqlAdministratorLogin = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_PASSWORD')

// API host: Basic B1, one instance - no daily CPU-minute quota (unlike Free/F1),
// trivial cost (~INR 1.72/hour) for a deployment that only needs to run for a few
// hours to capture evidence.
param apiSkuName = 'B1'
param apiSkuTier = 'Basic'
param apiSkuCapacity = 1

// SQL: General Purpose Serverless, 1 vCore, with the Azure SQL Database free monthly
// limit applied (100,000 vCore-seconds + 32 GB/month, confirmed live against
// https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer during
// Phase 2 - recurring monthly for the subscription's lifetime, not a 12-month
// offer). AutoPause on free-limit exhaustion, not BillForUsage, so an unexpectedly
// long test session degrades to "paused" rather than to full serverless billing.
// Short retention and locally-redundant backups - this database holds no data worth
// protecting past the test session.
param sqlComputeModel = 'Serverless'
param sqlSkuName = 'GP_S_Gen5'
param sqlCapacity = 1
param sqlUseFreeLimit = true
param sqlBackupRetentionDays = 7
param sqlBackupStorageRedundancy = 'Local'
param sqlZoneRedundant = false

// Service Bus: short message TTL and a short duplicate-detection window - nothing
// consumes these topics yet (see modules/servicebus.bicep's header comment), so
// there's no reason to hold messages or dedupe state longer than a dev session.
// Partitioning off: unnecessary complexity for a namespace with no real traffic.
param serviceBusMessageTtl = 'P1D'
param serviceBusDuplicateDetectionWindow = 'PT10M'
param serviceBusEnablePartitioning = false
