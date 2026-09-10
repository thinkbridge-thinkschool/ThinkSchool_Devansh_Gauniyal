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
// neither async flow is wired to a real queue). Through Day 25, no subscriptions
// existed here, deliberately - a subscription belongs to a real consumer, and there
// wasn't one yet.
//
// Day 26 changes that, narrowly: one subscription (`trace-demo-worker`, on
// `supplier-notifications`) for the minimal trace-demo worker described in
// ../README.md, "Day 26 - the trace-demo worker". This is NOT one of the two real
// async flows DESIGN.md names going live - it exists solely so a distributed trace
// has a genuine second hop to stitch across. The `invoice-approved-events` topic
// still has no subscription and no consumer; that flow remains exactly as
// undeveloped as it was through Day 25.
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

@description('Principal id of the API\'s user-assigned managed identity - granted Azure Service Bus Data Sender on this namespace. See "Day 25" comment below for why Sender only.')
param apiPrincipalId string

@description('Message TTL for the one Day 26 demo subscription, ISO 8601 duration. Deliberately short (dev default P1D) - messages sit here only long enough for the trace-demo worker to pick them up during a live session, never intended to accumulate.')
param demoSubscriptionMessageTtl string = 'P1D'

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

// Day 25: Azure Service Bus Data Sender, verified live via
// `az role definition list --name "Azure Service Bus Data Sender"` on
// 2026-09-09. Sender only, not Receiver or Owner: the API is the publisher
// for both topics named in capstone/DESIGN.md's async flows (supplier
// notification, the InvoiceApproved integration event) - there is no
// consumer yet (no subscriptions exist on either topic, deliberately, per
// the header comment above), so Receive capability has nothing to do and
// Owner would add management-plane rights (create/delete topics, change
// namespace settings) the application never needs. Scoped to the namespace
// rather than per-topic: this app needs to send to both existing topics, and
// Bicep's per-child-resource role assignment inside a `for` loop adds real
// complexity for a scope narrowing that would still cover 100% of what the
// app sends to - not worth it here, but named explicitly as the tighter
// alternative should a third, unrelated topic ever need excluding.
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'

// Day 26: Azure Service Bus Data Receiver, verified live via
// `az role definition list --name "Azure Service Bus Data Receiver"` on
// 2026-09-10. Granted to the SAME identity as Data Sender above (the trace-demo
// worker reuses the API's identity - see ../README.md, "Day 26 - the trace-demo
// worker" for why a second identity wasn't introduced for a demo-only component).
// Scoped to the one demo subscription below, not the namespace, so this new grant
// is exactly as narrow as the thing that needs it.
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
var demoSubscriptionTopicName = 'supplier-notifications'
var demoSubscriptionName = 'trace-demo-worker'

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
    // Day 25: was false (SAS keys allowed) through Day 24. Flipped to true -
    // identity (Azure AD/RBAC) is now the ONLY way to send or receive on this
    // namespace, closing the one remaining path a connection-string secret
    // could have existed on. See ../README.md, "What changed on Day 25".
    disableLocalAuth: true
  }
}

resource dataSenderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, apiPrincipalId, serviceBusDataSenderRoleId)
  scope: namespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
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

// Day 26: the trace-demo worker's one subscription - see header comment and
// ../README.md. Deliberately on `supplier-notifications`, not
// `invoice-approved-events`: either topic would work equally well as a bare
// transport for a demo message, and `supplier-notifications` was picked
// arbitrarily as the first of the two named in DESIGN.md. This choice carries no
// meaning about which real async flow is "closer" to being built.
resource demoSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2026-01-01' = {
  parent: topics[indexOf(topicNames, demoSubscriptionTopicName)]
  name: demoSubscriptionName
  properties: {
    defaultMessageTimeToLive: demoSubscriptionMessageTtl
    deadLetteringOnMessageExpiration: true
    maxDeliveryCount: 5
  }
}

resource dataReceiverAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(demoSubscription.id, apiPrincipalId, serviceBusDataReceiverRoleId)
  scope: demoSubscription
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceName string = namespace.name
output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output topicNames array = topicNames
output demoSubscriptionTopicName string = demoSubscriptionTopicName
output demoSubscriptionName string = demoSubscriptionName
