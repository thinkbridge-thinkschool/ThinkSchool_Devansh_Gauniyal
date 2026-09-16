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

// Day 25: same reasoning as dev.bicepparam - see that file's comment.
param entraAdminObjectId = readEnvironmentVariable('CAPSTONE_ENTRA_ADMIN_OBJECT_ID')
param entraAdminLogin = readEnvironmentVariable('CAPSTONE_ENTRA_ADMIN_LOGIN')
param keyVaultDemoSecretValue = readEnvironmentVariable('CAPSTONE_KV_DEMO_SECRET_VALUE')

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

// Day 26: 90-day retention (accepting the extra per-GB-per-month charge past the
// 31-day free window - a real production incident investigation needs lookback
// dev doesn't), a higher daily cap reflecting real traffic volume, and sampling
// below 100% (real production volume justifies trading some completeness for
// cost - see modules/monitoring.bicep and README.md). NOT deployed by this task -
// see header comment.
param logAnalyticsRetentionDays = 90
param dailyIngestionCapGb = 10
param otelSamplingRatio = '0.25'

param alertNotificationEmail = readEnvironmentVariable('CAPSTONE_ALERT_EMAIL')

// Tighter than dev - a real production error-rate deserves a lower bar and a
// higher severity than a dev demo.
param alertErrorRateThresholdPercent = 10
param alertSeverity = 1

// Day 27: same reasoning as dev.bicepparam - see that file's comment. A prod
// deployment would need its own app registration, not dev's - a real separate
// value, not reused across environments.
param apiAadAppId = readEnvironmentVariable('CAPSTONE_API_AAD_APP_ID')
