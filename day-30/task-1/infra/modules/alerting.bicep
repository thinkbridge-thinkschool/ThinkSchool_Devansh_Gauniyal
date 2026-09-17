// Error-rate alert module (Day 26): an Action Group plus a log-based Scheduled
// Query Rule against the Log Analytics workspace, so "an alert on error-rate" is
// declared in Bicep - not clicked together in the portal, consistent with every
// other resource in this project.
//
// Why a log alert (Microsoft.Insights/scheduledqueryrules, kind LogAlert) and not a
// classic metric alert on the "Failed requests" pre-aggregated metric: a log alert
// can run the exact same KQL shape as the diagnostic queries in
// ../../observability/queries/ (see 03-error-rate-by-endpoint.kql), so the alert and
// the "why did this fire" investigation query are the same language, not two
// different alerting systems to reason about.
//
// API versions verified live via `az provider show -n Microsoft.Insights` on
// 2026-09-10: scheduledqueryrules -> 2023-12-01 (stable; newer 2026-03-01 and
// 2025-01-01-preview also exist, not needed here), actiongroups -> 2023-01-01
// (stable; several -preview versions also exist, not needed here). Full property
// schemas confirmed live against
// https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/2023-12-01/scheduledqueryrules
// and .../2023-01-01/actiongroups on 2026-09-10 - not written from memory.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Resource id of the Log Analytics workspace this alert queries (from modules/monitoring.bicep).')
param logAnalyticsWorkspaceId string

@description('Notification email for the action group. No default - supplied at deploy time only via CAPSTONE_ALERT_EMAIL, exactly like the SQL credential in main.bicep, so a real address is never committed. See ../README.md.')
param notificationEmail string

@description('Error-rate threshold, as a percentage (0-100), that triggers the alert. See ../README.md for why 30 was chosen over a tighter production-style threshold.')
param errorRateThresholdPercent int = 30

@description('Minimum number of requests required in the evaluation window before the error rate is even computed - avoids a single failed request in a quiet dev environment reading as a "100% error rate" false alarm.')
param minimumRequestVolume int = 5

@description('How often the rule is evaluated, ISO 8601 duration.')
param evaluationFrequency string = 'PT5M'

@description('The trailing lookback window each evaluation covers, ISO 8601 duration. Wider than evaluationFrequency so a handful of scattered dev-session requests still land in one window together.')
param windowSize string = 'PT15M'

@description('Alert severity: 0 (critical) - 4 (verbose). 2 (Warning) - see ../README.md for why this is not Sev 0/1.')
param severity int = 2

@description('Tags applied to every resource in this module.')
param tags object

var actionGroupName = 'ag-capstone-${environmentName}-errors'
var alertName = 'alert-capstone-${environmentName}-error-rate'

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'CapAlerts'
    enabled: true
    emailReceivers: [
      {
        name: 'capstone-dev-oncall'
        emailAddress: notificationEmail
        useCommonAlertSchema: true
      }
    ]
    armRoleReceivers: []
    automationRunbookReceivers: []
    azureAppPushReceivers: []
    azureFunctionReceivers: []
    eventHubReceivers: []
    itsmReceivers: []
    logicAppReceivers: []
    smsReceivers: []
    voiceReceivers: []
    webhookReceivers: []
  }
}

// Overall API error rate, not split by endpoint - keeps the alert itself simple;
// the per-endpoint breakdown is a separate diagnostic query
// (../../observability/queries/03-error-rate-by-endpoint.kql) run by hand once this
// fires, not duplicated into the alert condition itself.
var errorRateQuery = '''
requests
| summarize Total = count(), Failed = countif(success == false)
| extend ErrorRatePercent = iif(Total > 0, round(100.0 * todouble(Failed) / Total, 2), 0.0)
| where Total >= {0}
| project ErrorRatePercent
'''

resource errorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: alertName
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: 'Capstone ${environmentName} API error rate'
    description: 'Fires when the API\'s request failure rate exceeds ${errorRateThresholdPercent}% over a ${windowSize} window, once at least ${minimumRequestVolume} requests have been seen in that window.'
    severity: severity
    enabled: true
    evaluationFrequency: evaluationFrequency
    windowSize: windowSize
    scopes: [
      logAnalyticsWorkspaceId
    ]
    criteria: {
      allOf: [
        {
          query: replace(errorRateQuery, '{0}', string(minimumRequestVolume))
          metricMeasureColumn: 'ErrorRatePercent'
          operator: 'GreaterThan'
          threshold: errorRateThresholdPercent
          timeAggregation: 'Maximum'
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    // Day 26 - what actually broke: the first deploy attempt failed here with
    // "Failed to resolve table or column expression named 'requests'." ARM
    // validates a scheduled query rule's KQL against the target workspace's
    // CURRENT schema at deploy time, and `requests` (an Application Insights
    // table) doesn't exist in a brand-new Log Analytics workspace until the
    // App Insights <-> workspace link finishes provisioning and/or the first
    // telemetry lands - both of which happen only after this same deployment
    // completes. Deploying the monitoring resources and this alert in one
    // shot therefore always loses this race on a fresh workspace. Verified by
    // reading the exact ARM error back (see infra/README.md's verification
    // log) rather than guessing; skipQueryValidation: true is the documented
    // way to defer that check to evaluation time, once real data exists - the
    // query itself is unchanged and correct.
    skipQueryValidation: true
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

output actionGroupId string = actionGroup.id
output actionGroupName string = actionGroup.name
output alertName string = errorRateAlert.name
