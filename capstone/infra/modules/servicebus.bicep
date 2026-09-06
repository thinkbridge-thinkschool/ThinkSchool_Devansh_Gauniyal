// The Service Bus module: a Standard-tier namespace with two topics matching the two
// genuinely async flows capstone/DESIGN.md names ("Async flows - kept deliberately
// small"):
//   1. supplier-notifications      - on approval/dispute; a convenience, not a
//                                     correctness dependency.
//   2. invoice-approved-events      - the InvoiceApproved integration event, written
//                                     to an outbox in the same transaction as
//                                     approval, for a Financing consumer that does
//                                     not exist yet.
//
// This is provisioned AHEAD of the application: nothing in capstone/src publishes to
// or consumes from these topics today (see capstone/README.md, "Messaging / outbox" -
// neither async flow is wired to a real queue). No subscriptions are created here,
// deliberately - a subscription belongs to a real consumer, and there isn't one yet;
// see ../README.md, "why Service Bus is provisioned ahead of the application."
//
// Standard is the minimum tier that supports topics at all (Basic is queues-only -
// confirmed via https://learn.microsoft.com/en-us/azure/service-bus-messaging/
// service-bus-premium-messaging, "Choosing a tier", read live during Phase 2's
// pricing investigation). Premium would add dedicated capacity and zone redundancy,
// but at ~88x Standard's base cost for a namespace nothing calls yet - not justified
// here; see ../README.md.
//
// API versions (Microsoft.ServiceBus/namespaces, namespaces/topics: 2026-01-01)
// verified live via `az provider show -n Microsoft.ServiceBus` on 2026-09-07, after
// registering the Microsoft.ServiceBus resource provider (it was NotRegistered on
// this subscription until this session) - see ../README.md.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Default message time-to-live for both topics, ISO 8601 duration (e.g. P1D for dev, P14D for prod).')
param messageTimeToLive string = 'P1D'

@description('Duplicate-detection history window, ISO 8601 duration (e.g. PT10M for dev, PT1H for prod).')
param duplicateDetectionWindow string = 'PT10M'

@description('Whether topics are partitioned across multiple message brokers for higher throughput. Off for dev (simpler to reason about and to read back in the portal); on for prod.')
param enablePartitioning bool = false

@description('Tags applied to every resource in this module.')
param tags object

// Service Bus namespace names are globally unique across all of Azure, so the
// suffix is deterministic per subscription/environment - see api.bicep's identical
// reasoning.
var uniqueSuffix = substring(uniqueString(subscription().id, environmentName, 'servicebus'), 0, 6)
var namespaceName = 'sb-capstone-${environmentName}-${uniqueSuffix}'

var topicNames = [
  'supplier-notifications'
  'invoice-approved-events'
]

resource namespace 'Microsoft.ServiceBus/namespaces@2026-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource topics 'Microsoft.ServiceBus/namespaces/topics@2026-01-01' = [
  for topicName in topicNames: {
    parent: namespace
    name: topicName
    properties: {
      defaultMessageTimeToLive: messageTimeToLive
      duplicateDetectionHistoryTimeWindow: duplicateDetectionWindow
      requiresDuplicateDetection: true
      enablePartitioning: enablePartitioning
    }
  }
]

output namespaceName string = namespace.name
output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output topicNames array = topicNames
