// Day 27 - the VNet, subnets, and SQL's private endpoint that take the data tier
// off the public internet. See ../README.md's Day 27 section and
// capstone/THREAT-MODEL.md's Information disclosure section for why: through
// Day 26, the SQL firewall rule (`AllowAllAzureServices`, 0.0.0.0-0.0.0.0) let ANY
// Azure-hosted resource in the world reach the server, and Service Bus had no
// network restriction at all - identity-based auth (Day 25) meant a stolen
// credential was useless, but network reachability itself was still wide open.
//
// Service Bus does NOT get a private endpoint here, despite the task asking for
// one on "the data tier" - confirmed live this session that
// `Microsoft.ServiceBus/namespaces` private endpoints are only supported on the
// Premium SKU (`az network private-endpoint create` against this project's real
// Standard namespace failed outright with `PrivateEndpointInvalidSku: "Private
// endpoint connections are only supported on Premium Service Bus namespaces"`).
// Premium's minimum SKU (1 messaging unit) prices at $0.9275/hour - roughly 69x
// Standard's cost, already named as unjustified for this namespace's real traffic
// in ../README.md's Day 23 section - and would have meaningfully eaten this
// subscription's remaining credit if left running per this task's "leave the dev
// stack running" instruction. Given that real trade-off, Service Bus instead gets
// a Virtual Network Rule (modules/servicebus.bicep) restricting it to only this
// VNet's App Service subnet - see that module's own comment for what this does
// and does not achieve compared to a true private endpoint.
//
// Microsoft.Network was NotRegistered on this subscription before this session -
// registered via `az provider register -n Microsoft.Network` (same pattern as
// Microsoft.Sql/Microsoft.ServiceBus on Day 23 - see ../README.md). API versions
// (virtualNetworks, privateEndpoints: 2026-03-01; privateDnsZones,
// privateDnsZones/virtualNetworkLinks: 2024-06-01) verified live via
// `az provider show -n Microsoft.Network` on 2026-09-11.

@description('Environment name, used in resource naming.')
param environmentName string

@description('Azure region, inherited from the resource group this module is scoped to.')
param location string

@description('Resource id of the SQL logical server (from modules/sql.bicep) to attach a private endpoint to.')
param sqlServerResourceId string

@description('Tags applied to every resource in this module.')
param tags object

var vnetName = 'vnet-capstone-${environmentName}'
var appServiceIntegrationSubnetName = 'snet-appservice-integration'
var privateEndpointSubnetName = 'snet-private-endpoints'

// /26 (64 addresses) for App Service regional VNet Integration - both Web Apps
// (API and worker, sharing one App Service plan - see modules/worker.bicep) use
// the same integration subnet; Azure's documented minimum is /27, this leaves
// headroom without wasting a meaningful part of the /16. /27 (32 addresses) for
// private endpoints - one today (SQL), room to add Key Vault's later without
// resizing (see ../README.md, "Honest limits" - Key Vault's private endpoint is a
// named, not-yet-built gap, same as before this day).
var addressSpace = '10.20.0.0/16'
var appServiceIntegrationSubnetPrefix = '10.20.1.0/26'
var privateEndpointSubnetPrefix = '10.20.2.0/27'

var sqlPrivateDnsZoneName = 'privatelink.database.windows.net'

resource vnet 'Microsoft.Network/virtualNetworks@2026-03-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressSpace]
    }
    subnets: [
      {
        name: appServiceIntegrationSubnetName
        properties: {
          addressPrefix: appServiceIntegrationSubnetPrefix
          // Required for regional VNet Integration - a Web App can only integrate
          // with a subnet delegated to its own service, and the subnet must be
          // empty of any other resource type.
          delegations: [
            {
              name: 'delegation-appservice'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
          // Day 27: what lets modules/servicebus.bicep's Virtual Network Rule
          // recognize traffic from this subnet as coming from an allowed network,
          // in place of the private endpoint Service Bus's Standard SKU can't
          // have - see this file's header comment. Without this service
          // endpoint, `ignoreMissingVnetServiceEndpoint: false` on that rule would
          // reject every request from this subnet, not just unrelated ones.
          serviceEndpoints: [
            {
              service: 'Microsoft.ServiceBus'
            }
          ]
        }
      }
      {
        name: privateEndpointSubnetName
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          // Private endpoints get their own NIC with a private IP from this
          // subnet; network policies (NSG/route table enforcement on the private
          // endpoint's NIC itself) must be disabled for that IP to be assignable -
          // confirmed live against Microsoft Learn's private endpoint networking
          // article on 2026-09-11.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: sqlPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource sqlDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDnsZone
  name: 'link-${vnetName}'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    // No auto-registration: this zone holds exactly one A record per private
    // endpoint (added by the privateDnsZoneGroup resources below), not every VM
    // in the VNet - there are no VMs in this VNet to register anyway.
    registrationEnabled: false
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2026-03-01' = {
  name: 'pe-capstone-${environmentName}-sql'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: '${vnet.id}/subnets/${privateEndpointSubnetName}'
    }
    privateLinkServiceConnections: [
      {
        name: 'sql-connection'
        properties: {
          privateLinkServiceId: sqlServerResourceId
          // 'sqlServer' is the fixed group id Microsoft.Sql/servers exposes for
          // private link - verified live via `az network private-link-resource
          // list --type Microsoft.Sql/servers` on 2026-09-11.
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2026-03-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: sqlPrivateDnsZoneName
        properties: {
          privateDnsZoneId: sqlPrivateDnsZone.id
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output appServiceIntegrationSubnetId string = '${vnet.id}/subnets/${appServiceIntegrationSubnetName}'
output sqlPrivateDnsZoneName string = sqlPrivateDnsZone.name
