# Day 5 Task 3 — Azure CLI commands and real output

All output below is copied from actual commands run against a real Azure subscription ("Azure for Students"). Nothing here is invented.

**Redaction note:** every subscription ID, tenant ID, and Log Analytics workspace ID has been replaced with a placeholder (`YOUR_SUBSCRIPTION_ID`, `YOUR_TENANT_ID`, `YOUR_WORKSPACE_ID`). The environment's custom-domain verification token and the account email in `systemData` were also redacted/omitted, even though not explicitly named in the task's redaction list, out of the same caution. No key, shared key, or connection string is included anywhere -- the one field that could carry one (`sharedKey`) was returned as `null` by Azure itself. The environment's default domain and static IP were redacted by default (see `notes.md` for what each would reveal if kept). These are placeholders, not errors -- ask if you want any of them explained further.

## Pre-flight checks

```
$ az provider show -n Microsoft.App --query "{namespace:namespace, registrationState:registrationState}" -o json
{
  "namespace": "Microsoft.App",
  "registrationState": "NotRegistered"
}

$ az containerapp env list -o table
ERROR: Subscription YOUR_SUBSCRIPTION_ID is not registered for the Microsoft.App resource provider. Please run "az provider register -n Microsoft.App --wait" to register your subscription.

$ az provider show -n Microsoft.OperationalInsights --query "{namespace:namespace, registrationState:registrationState}" -o json
{
  "namespace": "Microsoft.OperationalInsights",
  "registrationState": "Registered"
}

$ az account list-locations --query "[?name=='centralindia']" -o json
[
  {
    "displayName": "Central India",
    "name": "centralindia",
    "regionalDisplayName": "(Asia Pacific) Central India",
    "type": "Region"
    ... (subscription-scoped resource id omitted, not sensitive but not needed here)
  }
]

$ az group show -n thinkschool-rg
ERROR: (ResourceGroupNotFound) Resource group 'thinkschool-rg' could not be found.

$ az group list -o table
(empty -- no resource groups existed anywhere in this subscription before this task)
```

## Registration

```
$ az provider register -n Microsoft.App --wait
(no output -- exit code 0)

$ az provider show -n Microsoft.App --query "{namespace:namespace, registrationState:registrationState}" -o json
{
  "namespace": "Microsoft.App",
  "registrationState": "Registered"
}
```

## Resource group creation

```
$ az group create -n thinkschool-rg -l centralindia
{
  "id": "/subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/thinkschool-rg",
  "location": "centralindia",
  "managedBy": null,
  "name": "thinkschool-rg",
  "properties": {
    "provisioningState": "Succeeded"
  },
  "tags": null,
  "type": "Microsoft.Resources/resourceGroups"
}
```

## Container Apps environment creation

```
$ az containerapp env create -n thinkschool-env -g thinkschool-rg -l centralindia
WARNING: No Log Analytics workspace provided.
WARNING: Generating a Log Analytics workspace with name "workspace-thinkschoolrgP59G"
WARNING: Container Apps environment created. To deploy a container app, use: az containerapp create --help

{
  "id": "/subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env",
  "location": "Central India",
  "name": "thinkschool-env",
  "properties": {
    "appInsightsConfiguration": null,
    "appLogsConfiguration": {
      "destination": "log-analytics",
      "logAnalyticsConfiguration": {
        "customerId": "YOUR_WORKSPACE_ID",
        "sharedKey": null
      }
    },
    "customDomainConfiguration": {
      "certificateKeyVaultProperties": null,
      "certificatePassword": null,
      "certificateValue": null,
      "customDomainVerificationId": "YOUR_DOMAIN_VERIFICATION_ID",
      "dnsSuffix": null,
      "expirationDate": null,
      "subjectName": null,
      "thumbprint": null
    },
    "daprAIConnectionString": null,
    "daprAIInstrumentationKey": null,
    "daprConfiguration": { "version": "1.16.4-msft.11" },
    "defaultDomain": "YOUR_DEFAULT_DOMAIN",
    "eventStreamEndpoint": "https://centralindia.azurecontainerapps.dev/subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/thinkschool-rg/managedEnvironments/thinkschool-env/eventstream",
    "infrastructureResourceGroup": null,
    "ingressConfiguration": null,
    "kedaConfiguration": { "version": "2.18.1" },
    "openTelemetryConfiguration": null,
    "peerAuthentication": { "mtls": { "enabled": false } },
    "peerTrafficConfiguration": { "encryption": { "enabled": false } },
    "provisioningState": "Succeeded",
    "publicNetworkAccess": "Enabled",
    "staticIp": "YOUR_STATIC_IP",
    "vnetConfiguration": null,
    "workloadProfiles": [
      { "enableFips": false, "name": "Consumption", "workloadProfileType": "Consumption" }
    ],
    "zoneRedundant": false
  },
  "resourceGroup": "thinkschool-rg",
  "systemData": {
    "createdAt": "2026-08-14T06:58:52.0682035",
    "createdBy": "YOUR_ACCOUNT_EMAIL",
    "createdByType": "User",
    "lastModifiedAt": "2026-08-14T06:58:52.0682035",
    "lastModifiedBy": "YOUR_ACCOUNT_EMAIL",
    "lastModifiedByType": "User"
  },
  "type": "Microsoft.App/managedEnvironments"
}
```

## Verification: `az containerapp env show`

```
$ az containerapp env show -n thinkschool-env -g thinkschool-rg
```
Returned the identical JSON shown above (same resource, same redactions apply).

## What actually exists in the resource group

```
$ az resource list -g thinkschool-rg -o table
Name                         ResourceGroup    Location      Type                                      Status
---------------------------  ---------------  ------------  ----------------------------------------  ---------
workspace-thinkschoolrgP59G  thinkschool-rg   centralindia  Microsoft.OperationalInsights/workspaces  Succeeded
thinkschool-env              thinkschool-rg   centralindia  Microsoft.App/managedEnvironments         Succeeded
```
Confirms both the environment and its auto-created Log Analytics workspace live in `thinkschool-rg` -- deleting that resource group removes both.

## Teardown (not run in this task -- given for reference)

```
az group delete -n thinkschool-rg --yes
```
