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

// Day 25: the Entra ID principal set as this server's Entra administrator (a
// human - see ../README.md, "Manual step 1"). An object id and a UPN/display
// name are identifiers, not secrets, but stay out of this committed file the
// same way the SQL credential does - see ../README.md, "Supplying identity
// values."
param entraAdminObjectId = readEnvironmentVariable('CAPSTONE_ENTRA_ADMIN_OBJECT_ID')
param entraAdminLogin = readEnvironmentVariable('CAPSTONE_ENTRA_ADMIN_LOGIN')

// Day 25: proves the Key Vault reference mechanism resolves end-to-end - see
// modules/keyvault.bicep's header comment. Not a real application secret, but
// still supplied only at deploy time, never committed, like every other
// secure parameter in this file.
param keyVaultDemoSecretValue = readEnvironmentVariable('CAPSTONE_KV_DEMO_SECRET_VALUE')

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

// Day 26: Log Analytics retention at 30 days - one day inside the 31-day
// free-included window, so this project pays nothing extra for retention (see
// modules/monitoring.bicep). Daily ingestion cap at 1 GB/day - generous headroom
// above the low-hundreds-of-KB a deliberate traffic-generation session actually
// produces, while still bounding a runaway/misconfigured-logging scenario to a
// small, non-alarming fraction of the subscription's Free Trial credit (see
// README.md's billing section). No sampling (1.0 = 100%) - at this deliberately
// low dev volume, sampling would throw away a meaningful fraction of the very
// traffic this session generates to make the p50/p99 and error-rate queries mean
// something; see README.md for the trade-off this defers, not avoids.
param logAnalyticsRetentionDays = 30
param dailyIngestionCapGb = 1
param otelSamplingRatio = '1.0'

// Day 26: like the SQL credential above, no default - supplied at deploy time
// only via CAPSTONE_ALERT_EMAIL, so a real email address is never committed (this
// task's own instructions list "real emails" among the things to keep out of git).
param alertNotificationEmail = readEnvironmentVariable('CAPSTONE_ALERT_EMAIL')

// 30% over a 15-minute window, Sev 2 (Warning) - see modules/alerting.bicep and
// README.md for why these values, not tighter production-style thresholds.
param alertErrorRateThresholdPercent = 30
param alertSeverity = 2

// Day 27: the API's own Entra ID app registration id (see
// submission-day-27-task-1.md) - not a secret, but sourced from the environment
// at deploy time like every other identifier in this file, never committed.
param apiAadAppId = readEnvironmentVariable('CAPSTONE_API_AAD_APP_ID')
