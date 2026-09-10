// Observability module (Day 26): a Log Analytics workspace plus a workspace-based
// Application Insights resource, so the API and the trace-demo worker (see
// ../README.md, "Day 26 - the trace-demo worker") have somewhere real to export
// OpenTelemetry traces, metrics, and logs to.
//
// Workspace-based, not classic: classic Application Insights (its own isolated data
// store) has been the deprecated shape since 2024 - workspace-based routes all
// telemetry through the same Log Analytics workspace every other Azure diagnostic
// setting in this subscription would use, so KQL against `requests`/`dependencies`/
// `traces` and KQL against, say, App Service platform logs, run in one place.
//
// Ingestion protection is layered twice, deliberately - confirmed live via
// https://learn.microsoft.com/en-us/azure/azure-monitor/logs/daily-cap on
// 2026-09-10: "For workspace-based Application Insights resources, the effective
// daily cap is the minimum of the two settings" (the workspace's own cap and the
// component's own cap). Setting only one and assuming it covers the other is a
// documented gap, not a safe simplification.
//
// API versions verified live via `az provider show` on 2026-09-10, on this
// subscription:
//   - Microsoft.OperationalInsights/workspaces: 2025-02-01 (latest stable; 2026-03-01
//     also exists but is unneeded here - no property this module sets requires it)
//   - Microsoft.Insights/components: 2020-02-02 (latest stable; 2020-02-02-preview
//     also exists, not needed)
//   - Microsoft.Insights/components/currentbillingfeatures: 2020-02-02-preview is
//     the ONLY api-version this child resource type exposes on this subscription -
//     confirmed via `az provider show -n Microsoft.Insights`, not assumed.
// Monitoring Metrics Publisher role id (3913510d-42f4-4e42-8a64-420c390055eb)
// verified live via `az role definition list --name "Monitoring Metrics Publisher"`
// on 2026-09-10 - this is the role Microsoft Learn's Entra-ID-authenticated-ingestion
// guide names as the one the exporting identity needs (despite the "Metrics" name, it
// authorizes all telemetry types, not metrics alone - the guide is explicit about
// this).

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Log Analytics data retention, in days. Dev uses 30 - the free-included retention window (31 days) minus a one-day margin, so this project pays nothing extra for retention while still covering a full work week of history. Prod would use 90+ to support real incident investigation lookback, accepting the extra per-GB-per-month retention charge that implies - see ../README.md.')
param logAnalyticsRetentionDays int

@description('Daily ingestion cap, in GB, applied to BOTH the Log Analytics workspace and the Application Insights component (belt and suspenders - see header comment on why one alone is not provably sufficient). See ../README.md for the reasoning behind the chosen value per environment.')
param dailyIngestionCapGb int

@description('Percentage (0-100) of the daily cap at which a warning is recorded, before collection actually stops.')
param dailyCapWarningThresholdPercent int = 80

@description('Principal id of the API/worker user-assigned managed identity - granted Monitoring Metrics Publisher on this Application Insights resource so Entra-ID-authenticated ingestion (see host/Capstone.Web/Program.cs and host/Capstone.Worker/Program.cs) has something to authorize against, instead of relying solely on the connection string.')
param apiPrincipalId string

@description('Tags applied to every resource in this module.')
param tags object

var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'monitoring'), 0, 6)
var workspaceName = 'log-capstone-${environmentName}-${uniqueSuffix}'
var appInsightsName = 'appi-capstone-${environmentName}-${uniqueSuffix}'
var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logAnalyticsRetentionDays
    workspaceCapping: {
      dailyQuotaGb: dailyIngestionCapGb
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    // Day 25 set disableLocalAuth: true on Service Bus so identity is the only way
    // in - see modules/servicebus.bicep. Same choice here: this forces every
    // ingestion request to carry a Microsoft Entra token (from the managed
    // identity's Credential, set in code - see host/Capstone.Web/Program.cs),
    // never the connection string's instrumentation key alone. Verified live via
    // https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication
    // on 2026-09-10 ("Disable local authentication... Use the DisableLocalAuth
    // property").
    DisableLocalAuth: true
    RetentionInDays: logAnalyticsRetentionDays
  }
}

// See header comment: this is the SECOND of the two caps a workspace-based
// resource needs - the effective cap is the minimum of this and the workspace's
// own workspaceCapping.dailyQuotaGb above.
resource appInsightsDailyCap 'Microsoft.Insights/components/currentbillingfeatures@2020-02-02-preview' = {
  parent: appInsights
  name: 'current'
  properties: {
    CurrentBillingFeatures: [
      'Basic'
    ]
    DataVolumeCap: {
      Cap: dailyIngestionCapGb
      WarningThreshold: dailyCapWarningThresholdPercent
      StopSendNotificationWhenHitThreshold: false
    }
  }
}

resource monitoringMetricsPublisherAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appInsights.id, apiPrincipalId, monitoringMetricsPublisherRoleId)
  scope: appInsights
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output logAnalyticsWorkspaceId string = logAnalytics.id
output logAnalyticsWorkspaceName string = logAnalytics.name
output appInsightsName string = appInsights.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsId string = appInsights.id
