# Day 5 Task 3 — Azure CLI commands and real output

All output below is copied from actual commands run against a real Azure subscription ("Azure for Students"). Nothing here is invented.

**Redaction note:** the subscription ID is shown in full below (`7cf66c88-43bb-432a-ad87-0f5c63589d68`) at the user's explicit request -- it's an identifier, not a credential, and no access is granted by knowing it alone. The Log Analytics workspace ID, the environment's custom-domain verification token, and the account email in `systemData` remain redacted/omitted as placeholders (`YOUR_WORKSPACE_ID`, `YOUR_DOMAIN_VERIFICATION_ID`, `YOUR_ACCOUNT_EMAIL`), even though not all of these were explicitly named in the task's redaction list, out of the same caution. No key, shared key, or connection string is included anywhere -- the one field that could carry one (`sharedKey`) was returned as `null` by Azure itself. The environment's default domain and static IP remain redacted (see `notes.md` for what each would reveal if kept). Tenant ID never actually appeared in any of this file's captured command output. These placeholders are intentional, not errors -- ask if you want any of them explained further.

## Pre-flight checks

```
$ az provider show -n Microsoft.App --query "{namespace:namespace, registrationState:registrationState}" -o json
{
  "namespace": "Microsoft.App",
  "registrationState": "NotRegistered"
}

$ az containerapp env list -o table
ERROR: Subscription 7cf66c88-43bb-432a-ad87-0f5c63589d68 is not registered for the Microsoft.App resource provider. Please run "az provider register -n Microsoft.App --wait" to register your subscription.

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
  "id": "/subscriptions/7cf66c88-43bb-432a-ad87-0f5c63589d68/resourceGroups/thinkschool-rg",
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
  "id": "/subscriptions/7cf66c88-43bb-432a-ad87-0f5c63589d68/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env",
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
    "eventStreamEndpoint": "https://centralindia.azurecontainerapps.dev/subscriptions/7cf66c88-43bb-432a-ad87-0f5c63589d68/resourceGroups/thinkschool-rg/managedEnvironments/thinkschool-env/eventstream",
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
