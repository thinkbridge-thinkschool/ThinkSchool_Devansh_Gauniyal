# Capstone infrastructure (Day 23 Task 1 — Bicep IaC; Day 24 Task 1 — Deployment Stacks + azd; Day 25 Task 1 — identity end-to-end)

Bicep templates for the capstone's API host, SQL database, Service Bus
namespace, managed identity, and Key Vault, deployed as Azure Deployment
Stacks and configured through azd. Lives at `capstone/infra/`, not a
`day-N/task-M` folder — see "Why this lives here" below, and see "Repository
layout convention" for how `day-N/task-M/` snapshots relate to this live
folder.

## What each module does and why

- **[`main.bicep`](main.bicep)** — subscription-scoped composition root. Creates its
  own resource group (`rg-capstone-{env}`, never `rg-thinkschool-d17-t1` or
  `DefaultResourceGroup-CID` — those are unrelated, already-live Day 17
  infrastructure this template never references or modifies) and deploys the three
  modules into it, passing every SKU/capacity/retention/redundancy value through as a
  parameter. Nothing environment-specific is hardcoded here — that all lives in
  `parameters/dev.bicepparam` / `parameters/prod.bicepparam`.
- **[`modules/api.bicep`](modules/api.bicep)** — a Linux App Service plan plus one
  Linux Web App, running the .NET 10 runtime stack (`DOTNETCORE|10.0`) that
  `capstone/host/Capstone.Web` targets. This provisions the host; it does not deploy
  application code — that's a separate step (e.g. `az webapp deploy` or a
  CI/CD pipeline), out of scope for an infrastructure-as-code task.
- **[`modules/sql.bicep`](modules/sql.bicep)** — a SQL logical server, one database,
  a firewall rule allowing Azure-hosted callers through, and a short-term (PITR)
  backup retention policy. Sized to eventually hold the `Invoice` and
  `PurchaseOrder` aggregates `capstone/DESIGN.md` describes — provisioned **ahead**
  of the persistence code, which doesn't exist yet (both repositories are still
  in-memory; see `capstone/README.md`, "what's deliberately not built yet").
- **[`modules/servicebus.bicep`](modules/servicebus.bicep)** — a Standard-tier
  namespace with two topics, `supplier-notifications` and `invoice-approved-events`,
  matching the two async flows named in `capstone/DESIGN.md` ("Async flows — kept
  deliberately small"). See "Why Service Bus is provisioned ahead of the
  application" below — this is infrastructure for flows that are designed, not
  wired. As of Day 25, `disableLocalAuth: true` — see "Identity, Key Vault, and
  zero secrets" below.
- **[`modules/identity.bicep`](modules/identity.bicep)** *(Day 25)* — the API's
  one user-assigned managed identity. Every other Day 25 module and the app
  settings below reference its outputs.
- **[`modules/keyvault.bicep`](modules/keyvault.bicep)** *(Day 25)* — an
  RBAC-authorized Key Vault holding one demo secret, plus the role assignment
  granting the API's identity read access to it. See "Identity, Key Vault, and
  zero secrets" below for why this vault exists even though nothing in this
  scaffold has a real secret left to put in it.

## Why this lives at `capstone/infra/`, not `day-23/task-1`

The capstone is one continuous project spanning Day 22 through Days 28–32 (see
`capstone/README.md`, "Why this lives at `capstone/`, not `day-22/task-2`" — the same
reasoning applies to every later Academy day that touches it). Infrastructure
belongs beside the application code it describes, not in a separate day-numbered
folder duplicated or drifting from what `capstone/` actually contains. Each day's
submission file (`capstone/submission-day-23-task-1.md`, following
`submission-day-22-task-2.md`) documents the state of `capstone/infra/` as of that
day.

## Why Service Bus is provisioned ahead of the application

`capstone/DESIGN.md` names exactly two genuinely async flows: supplier notification
on approval/dispute, and the `InvoiceApproved` integration event for a Financing
consumer that doesn't exist yet. Neither is wired to a real queue in
`capstone/src` today (`capstone/README.md`: "Messaging / outbox... neither is wired
to a real queue or outbox table"). This module provisions the two topics those
flows will eventually publish to, so the infrastructure exists before the code that
needs it — but nothing in `capstone/src` currently publishes to or consumes from
them. No subscriptions are created here, deliberately: a subscription belongs to a
real consumer, and no consumer exists yet. This is infrastructure ahead of the
application, not infrastructure in use — treat it as scaffolding for Days 28–32,
not evidence that messaging is wired today.

## Every parameter, and what it controls

| Parameter | Controls | Dev | Prod |
|---|---|---|---|
| `environmentName` | Resource naming, tag values | `dev` | `prod` |
| `location` | Azure region for every resource | `centralindia` | `centralindia` |
| `resourceGroupName` | The resource group this deployment creates | `rg-capstone-dev` | `rg-capstone-prod` |
| `sqlAdministratorLogin` | SQL server admin login | from `CAPSTONE_SQL_ADMIN_LOGIN` | from `CAPSTONE_SQL_ADMIN_LOGIN` |
| `sqlAdministratorPassword` | SQL server admin password (`@secure()`) | from `CAPSTONE_SQL_ADMIN_PASSWORD` | from `CAPSTONE_SQL_ADMIN_PASSWORD` |
| `apiSkuName` / `apiSkuTier` / `apiSkuCapacity` | App Service plan SKU and instance count | `B1` / `Basic` / 1 | `S1` / `Standard` / 2 |
| `sqlComputeModel` | Serverless (auto-pauses) vs Provisioned (always on) | `Serverless` | `Provisioned` |
| `sqlSkuName` | SQL sku.name | `GP_S_Gen5` | `GP_Gen5` |
| `sqlCapacity` | vCores | 1 | 4 (smallest provisioned Gen5 size Central India offers) |
| `sqlUseFreeLimit` | Applies the Azure SQL Database free monthly limit | `true` | `false` |
| `sqlBackupRetentionDays` | PITR retention window, days | 7 | 35 (General Purpose max) |
| `sqlBackupStorageRedundancy` | Backup storage redundancy | `Local` | `Geo` |
| `sqlZoneRedundant` | Database zone redundancy | `false` | `true` |
| `entraAdminObjectId` *(Day 25)* | Object id of the Entra ID principal set as the SQL server's Entra administrator | from `CAPSTONE_ENTRA_ADMIN_OBJECT_ID` | from `CAPSTONE_ENTRA_ADMIN_OBJECT_ID` |
| `entraAdminLogin` *(Day 25)* | Display name / UPN of that same principal | from `CAPSTONE_ENTRA_ADMIN_LOGIN` | from `CAPSTONE_ENTRA_ADMIN_LOGIN` |
| `keyVaultDemoSecretValue` *(Day 25, `@secure()`)* | Value of the one demo secret proving the Key Vault reference mechanism resolves | from `CAPSTONE_KV_DEMO_SECRET_VALUE` | from `CAPSTONE_KV_DEMO_SECRET_VALUE` |
| `serviceBusMessageTtl` | Topic default message TTL (ISO 8601) | `P1D` | `P14D` |
| `serviceBusDuplicateDetectionWindow` | Dedupe history window (ISO 8601) | `PT10M` | `PT1H` |
| `serviceBusEnablePartitioning` | Topic partitioning across brokers | `false` | `true` |
| `tags` | Applied to every resource (see "Tagging" below) | environment-aware default | environment-aware default |

## How dev and prod differ, and why

> **Day 24 caveat:** the committed `parameters/prod.bicepparam` below still
> specifies the real `GP_Gen5`/4-vCore/zone-redundant values a genuine
> production subscription would use. The one real prod deploy done in this
> session used a CLI-only parameter override (`sqlSkuName=S0
> sqlSkuTier=Standard sqlZoneRedundant=false`) because this subscription is a
> Free Trial, which cannot provision vCore-based SQL databases outside the
> free-monthly-limit offer dev uses — see `VERIFICATION-LOG.md` §5. Nothing in
> this file changed as a result.

Every difference is a real operational one, not a name swap:

- **API host**: dev is Basic B1 (no daily CPU-minute quota, unlike Free/F1 — see the
  pricing investigation in the Day 23 submission notes; trivial cost for a short
  validation session). Prod is Standard S1 with 2 instances (autoscale/slots
  available, real redundancy).
- **SQL compute model**: dev is Serverless with `useFreeLimit: true` and
  `freeLimitExhaustionBehavior: AutoPause` — it auto-pauses (compute cost drops to
  zero) rather than silently billing at full serverless rates once the free monthly
  allowance is used. Prod is Provisioned at a fixed 4 vCores — a production
  database should never auto-pause mid-request.
- **SQL redundancy/retention**: dev keeps locally-redundant backups and a 7-day PITR
  window (this database holds no data worth protecting past a test session). Prod
  is zone-redundant with geo-replicated backups and the maximum 35-day General
  Purpose retention window.
- **Service Bus**: same tier (Standard) in both — see below for why — but dev uses a
  1-day message TTL, a 10-minute dedupe window, and no partitioning (nothing
  consumes these topics yet, so there's no reason to hold state longer than a dev
  session). Prod uses a 14-day TTL, a 1-hour dedupe window, and partitioning on for
  throughput headroom.

**Why Service Bus SKU does *not* differ between dev and prod**: Basic doesn't
support topics at all (confirmed live against
[Microsoft Learn's tier comparison](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-premium-messaging)
during the Day 23 pricing investigation), so Standard is the *minimum* viable tier
for either environment, not a dev choice. Premium adds dedicated messaging units,
VNet integration, and geo-replication — real production capabilities, but priced
per messaging-unit-hour at roughly 69x Standard's base charge, unjustified for a
namespace nothing calls yet in either environment. If a real production message
volume or a private-networking requirement ever arrives, Premium is the honest
upgrade path — not something to fake by setting the tier now.

## Supplying the SQL credential

`sqlAdministratorLogin` and `sqlAdministratorPassword` have **no default value**
anywhere — not in `main.bicep`, not in either `.bicepparam` file. Both parameter
files read them from the environment at deploy time:

```bicep
param sqlAdministratorLogin = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('CAPSTONE_SQL_ADMIN_PASSWORD')
```

Before running any `build-params`, `what-if`, or `deploy` command against either
parameter file, export both in the same shell session:

```bash
export CAPSTONE_SQL_ADMIN_LOGIN='your-admin-login'
export CAPSTONE_SQL_ADMIN_PASSWORD='a-strong-password'
```

If either is unset, Bicep fails the build immediately with `BCP427: Environment
variable "..." does not exist and there's no default value set` — verified directly
during Phase 4 (see `VERIFICATION-LOG.md`) — rather than silently deploying with a
placeholder credential.

**For the real dev deployment in this session**: a password was generated locally
with `openssl rand -base64 24` plus a fixed suffix to satisfy Azure SQL's complexity
rule, exported only as an environment variable for the deploy command, and **never
written to any file inside this repository**. It is not reproduced anywhere in this
README, the submission file, or any commit. Azure SQL does not expose an admin
password after creation — there is no "retrieve" operation. To rotate it:

```bash
az sql server update \
  --resource-group rg-capstone-dev \
  --name sql-capstone-dev-zpx4al \
  --admin-password 'a-new-strong-password'
```

## Tagging

Every resource carries `project: capstone`, `managed-by: bicep`,
`environment: <dev|prod>`, and `source: capstone/infra` (see `main.bicep`'s `tags`
parameter default) — enough to find and delete everything this template creates
with one query:

```bash
az resource list --tag project=capstone -o table
```

## Commands

All commands run from `capstone/infra/`, with the SQL credential environment
variables exported first (see above).

**Lint / build** (also performed by `what-if`/`deploy`, but useful standalone):
```bash
az bicep lint --file main.bicep
az bicep build-params --file parameters/dev.bicepparam --outfile /tmp/dev.params.json
```

**What-if** (subscription-scoped — this template creates its own resource group):
```bash
az deployment sub what-if \
  --name capstone-infra-dev-whatif \
  --location centralindia \
  --template-file main.bicep \
  --parameters parameters/dev.bicepparam
```
Replace `dev` with `prod` to validate the prod parameters — **`prod` is validated
only; it is never deployed by this task.**

**Deploy** (dev only):
```bash
az deployment sub create \
  --name capstone-infra-dev-deploy \
  --location centralindia \
  --template-file main.bicep \
  --parameters parameters/dev.bicepparam
```

**Tear down**:
```bash
az group delete --name rg-capstone-dev --yes --no-wait
```
This deletes every resource this deployment created, in one command, because they
all live in one resource group this template owns exclusively — nothing else was
ever placed in it and nothing pre-existing was ever moved into it.

## What's deliberately not in this infrastructure yet

Per the task: this is infrastructure-as-code for a still-in-memory application, not
a production rollout. Deliberately absent:

- **Private networking.** The SQL firewall rule (`AllowAllAzureServices`,
  `0.0.0.0`–`0.0.0.0`) allows any Azure-hosted resource through, because the Web App
  has no static outbound IP without VNet integration. A production version needs
  VNet integration on the Web App plus a private endpoint on the SQL server, with
  public network access disabled entirely.
- **CI-driven deployment.** Day 24's commands are still run by hand from a
  developer machine — `azd`/`az stack` themselves don't require a human at the
  keyboard, but nothing here runs them automatically. A production pipeline
  would run `what-if`/stack `validate` on every pull request and `deploy` only
  from a protected branch, with the SQL credential coming from a pipeline
  secret store, not a locally exported environment variable.
- **Approval gates between dev and prod.** Today's "promotion" is one person
  running one more command with `prod` parameters, immediately after dev. A
  real pipeline would require an approval step (a human sign-off, a set of
  passing integration tests, or both) between the two, and separate credentials
  per environment so a compromised dev credential can't touch prod.

**Managed identity wiring and Key Vault were the two items on this list as of
Day 24 — both are now done; see "Identity, Key Vault, and zero secrets (Day
25)" below for what changed, and its own "Honest limits, even after Day 25"
subsection for what's still missing (private endpoints on Key Vault/Service
Bus, network restrictions, secret rotation, break-glass access).**

See `VERIFICATION-LOG.md` for what was actually checked while building this —
Day 23's section covers the original Bicep authoring (one real `what-if`
fidelity quirk, not a bug); Day 24's section covers Deployment Stacks and azd
(a real, unresolved azd auth limitation, a transient Azure control-plane
glitch, and a real Free Trial subscription quota wall hit while promoting to
prod, each with exactly what was checked and what fixed or didn't fix it).

## Deployment Stacks (Day 24)

### What a Deployment Stack gives you over a plain deployment

Day 23 deployed with plain `az deployment sub create` — once it finished, Azure
forgot that "this exact set of resources belongs together and came from this
template." Nothing stopped someone from deleting one resource by hand, and
nothing recorded which resources a given deploy actually owned. A
[Deployment Stack](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deployment-stacks)
is itself a real, first-class Azure resource (`Microsoft.Resources/deploymentStacks`)
that keeps owning that set after the deploy finishes:

- **An explicit ownership record.** `az stack sub show --name capstone-dev`
  lists exactly which resources this deploy manages, right now, queryable at
  any time — not inferred from tags or a resource group's contents.
- **Clean teardown.** `az stack sub delete` removes every managed resource (and,
  per this project's `actionOnUnmanage` choice, the resource group too) in one
  command, because the stack — not a human remembering what a naming
  convention implies — knows exactly what it owns.
- **Drift protection and correction**, via `denySettings` — see below.

### The deny-settings and action-on-unmanage choices, and why

Both stacks in this project (`capstone-dev`, `capstone-prod`) use:

```
--action-on-unmanage deleteAll
--deny-settings-mode denyDelete
--deny-settings-apply-to-child-scopes
```

- **`actionOnUnmanage: deleteAll`** (not `detachAll`/`deleteResources`) — when
  the stack is deleted, or a resource is removed from the template on a future
  update, both the resource **and** its resource group should actually be
  deleted, not left behind as a billable orphan. This project's resource group
  exists solely to hold this stack's resources (`main.bicep` creates it, per
  Day 23), so detaching it on unmanage would leave an empty group nobody asked
  to keep.
- **`denySettings.mode: denyDelete`** (not `none` or `denyWriteAndDelete`) —
  deliberately in between the two extremes. `none` gives no protection at all.
  `denyWriteAndDelete` would have blocked the drift demonstration below
  outright (no property write could ever succeed out-of-band, including the
  test tag change) — real protection, but it leaves nothing to demonstrate
  about *correcting* drift, only *preventing* it. `denyDelete` blocks the
  destructive case (see the real rejected `az servicebus namespace delete`
  call in `VERIFICATION-LOG.md`, §6) while still allowing a property write
  (like a tag) to happen out-of-band — so the *other* half of drift, "someone
  changed something, and the next deploy corrects it," is real and
  demonstrable rather than blocked before it can happen.
- **`applyToChildScopes: true`** — without this, a SQL Server's own deny
  assignment wouldn't extend to its child `databases`/`firewallRules`
  resources, since those are technically their own resource types nested
  under the server. This project has exactly that shape (server → database,
  server → firewall rule), so this flag is load-bearing, not defensive.

### What "drift" means here, and how it's actually shown (not simulated)

Azure Deployment Stacks do **not** have a CloudFormation-style passive
drift-detection feed that continuously watches for out-of-band changes and
reports them on its own — that was confirmed by research before attempting
this demo (searched specifically for it; the closest existing feature request
against the deployment-stacks project is still open). What a stack genuinely
gives you is two different, real mechanisms, both demonstrated live for this
project (commands and captured output in `VERIFICATION-LOG.md` §6):

1. **Prevention** — `denySettings` actively rejects a disallowed out-of-band
   operation at the ARM authorization layer, before it ever executes. Proven
   with a real `az servicebus namespace delete` against a stack-managed
   resource, which failed with `DenyAssignmentAuthorizationFailed` naming the
   stack that owns it.
2. **Correction on redeploy** — an operation `denySettings` doesn't block
   (like a tag write) can still happen out-of-band; the stack doesn't notice
   until the next time it's applied, at which point the redeploy resets the
   drifted property back to whatever the template declares. Proven by tagging
   a resource out-of-band, confirming the tag live, then re-running the exact
   same `az stack sub create` and confirming the tag was gone.

Both are real, verified Azure behavior — neither is faked or narrated as if it
were the other.

### What azd adds over raw `az` commands (and what didn't work this session)

`azd`'s `azure.yaml` is where the Deployment Stacks configuration above
(`deploymentStacks.actionOnUnmanage`/`denySettings`) actually lives as
project-level, committed configuration — instead of remembering to type five
extra flags correctly on every `az stack sub create` call, `azd provision`
would read them from `azure.yaml` and apply them automatically, every time,
for every environment. It's also what gives this project the `dev`/`prod`
**environment** concept (`azd env new dev`, `azd env new prod`, `azd env
select ...`), each with its own `.azure/<env>/.env` holding
`AZURE_SUBSCRIPTION_ID`/`AZURE_LOCATION`/`AZURE_ENV_NAME` — so switching which
environment you're operating on is one command instead of five different
flags to keep track of by hand.

**Honestly: `azd` itself did not execute anything against Azure this
session.** `azd auth login` (a separate credential store from the `az` CLI)
could not be completed — two real device-code attempts, both confirmed
completed in the browser, both left `azd auth login --check-status` reporting
"Not logged in" (see `VERIFICATION-LOG.md` §2 for the exact commands and
codes). Everything from provisioning through teardown was run with `az stack
sub` directly, against the same `main.bicep` and the same
`parameters/dev.bicepparam`/`prod.bicepparam` files `azure.yaml` points
`infra.path`/`infra.module` at. The `azure.yaml` config, the `dev.bicepparam`/
`prod.bicepparam` wiring, and the deny-settings/action-on-unmanage values are
all real and correct — they're just not the thing that actually executed the
deploys in this session.

### How azd environments relate to the Bicep parameter files

Two layers, and it's worth being explicit about which controls which:

- **The `.bicepparam` files** (`parameters/dev.bicepparam`,
  `parameters/prod.bicepparam`, both from Day 23, unchanged) are the source of
  truth for every actual value — SKU, capacity, retention, redundancy, the
  `readEnvironmentVariable()` calls for the SQL credential. Nothing about
  these values lives anywhere else.
- **azd's environment concept** (`dev`/`prod` as azd environments) controls
  *which* `.bicepparam` file is in effect for a given `azd provision` run — and
  nothing more. The `preprovision` hook in `azure.yaml`
  (`infra/scripts/select-environment-parameters.sh`) copies
  `parameters/$AZURE_ENV_NAME.bicepparam` to `infra/main.bicepparam` — the
  filename azd's Bicep provider looks for by convention (confirmed empirically,
  see `VERIFICATION-LOG.md` §3) — before every provision. No parameter value
  is duplicated into `azure.yaml` or into azd's own environment config; the
  hook only *selects* one of the two existing files.

### Command sequence: provision, deploy, detect drift, tear down

All commands run from `capstone/infra/`, against whichever subscription
`az account show` currently reports as active, credentials exported first
(see "Supplying the SQL credential" above).

```bash
# Provision dev as a Deployment Stack
az stack sub create \
  --name capstone-dev --location centralindia \
  --template-file main.bicep --parameters parameters/dev.bicepparam \
  --action-on-unmanage deleteAll --deny-settings-mode denyDelete \
  --deny-settings-apply-to-child-scopes \
  --description "Capstone dev environment - API host, SQL, Service Bus"

# Verify what the stack actually owns
az stack sub show --name capstone-dev --query resources

# Prevention: an out-of-band delete of a managed resource is rejected
az servicebus namespace delete -g rg-capstone-dev -n <namespace-name>

# Drift: an out-of-band write succeeds until the next apply corrects it
az resource tag --ids <resource-id> --tags <existing-tags> drift-test=out-of-band-change
az stack sub create --name capstone-dev ... # (same command as above) - tag reverts

# Promote to prod - same template, new parameter file, new resource group
az stack sub create \
  --name capstone-prod --location centralindia \
  --template-file main.bicep --parameters parameters/prod.bicepparam \
  --action-on-unmanage deleteAll --deny-settings-mode denyDelete \
  --deny-settings-apply-to-child-scopes \
  --description "Capstone prod environment - API host, SQL, Service Bus"

# Tear down (deletes every managed resource AND the resource group)
az stack sub delete --name capstone-dev --action-on-unmanage deleteAll --yes
az stack sub delete --name capstone-prod --action-on-unmanage deleteAll --yes
```

If `azd` auth is ever resolved, the equivalent flow is `azd env select dev`,
`azd provision`, `azd env select prod`, `azd provision`, `azd down --environment
dev`, `azd down --environment prod` — see the alpha-feature note in
`azure.yaml`'s header comment.

## Identity, Key Vault, and zero secrets (Day 25)

The task: no connection-string secrets anywhere, managed identity for the
API's SQL and Service Bus paths, Entra ID for app auth, Key Vault references
for whatever config is left. This section is the "prove it" companion to
`modules/identity.bicep`, `modules/keyvault.bicep`, and the identity/Key
Vault/Entra-admin additions to `modules/sql.bicep`, `modules/servicebus.bicep`,
and `modules/api.bicep` — every claim below has a live command or a captured
file backing it, listed alongside it, per this task's "verify every surface
live, don't state anything from memory" rule.

**This is infrastructure wiring, not application code.** `capstone/src` has no
real data-access layer yet (both repositories are still in-memory — see
`capstone/README.md`). Nothing below claims the application uses this
identity today; it claims the identity, its grants, and the connection path
it enables all genuinely work, ready for the day the application code catches
up to the infrastructure — the same "ahead of the application" pattern this
project already uses for Service Bus (see above).

### Why a user-assigned identity, not system-assigned

`modules/identity.bicep` creates one `Microsoft.ManagedIdentity/userAssignedIdentities`
resource, deployed *before* the Web App that will use it. A system-assigned
identity is created and destroyed with its Web App — every grant made against
it (the Key Vault role, the Service Bus role, and especially the one manual
SQL grant a human has to run by hand, since it can't be automated — see
below) would have to be redone on every future App Service recreation. One
user-assigned identity, referenced by the Web App via its resource id, outlives
that: the Web App can be deleted and recreated (a slot swap, a region move, a
plan-tier change requiring a new site) without touching a single grant.

### Exact roles granted, and why each is the minimum

| Resource | Role | Role definition id | Scope | Why this role and not a broader one |
|---|---|---|---|---|
| Key Vault | Key Vault Secrets User | `4633458b-17de-408a-b874-0445c86b69e6` | the vault only | Get/List secrets only — no Set/Delete on a secret, no vault management. The API only ever *reads* config. |
| Service Bus namespace | Azure Service Bus Data Sender | `69a216fc-b8fb-44d8-bc22-1f3c2cd27a39` | the namespace | The API is the publisher for both topics (see "Why Service Bus is provisioned ahead of the application") — there is no consumer yet, so Receive has nothing to do, and Data Owner would add entity-management rights (create/delete queues and topics) the application never needs. Verified live in `output/identity-connectivity-proof.txt` §3a: the same identity's token is rejected with `Authorization failed for specified action: Manage,EntityWrite` when it tries to create a queue. |
| SQL database | `db_datareader` + `db_datawriter` (T-SQL database roles, not an ARM role assignment) | n/a — SQL's own permission model | the one `sqldb-capstone-{env}` database | No `db_owner`, no server-level role. Azure SQL access control for a *specific database's* rows lives in T-SQL roles, not `Microsoft.Authorization/roleAssignments` — ARM RBAC on a SQL server only reaches as far as the `Microsoft.Sql/servers/administrators` designation (see below), never into an individual database's permissions. |

Both Key Vault and Service Bus role assignments are declared directly in
`modules/keyvault.bicep` / `modules/servicebus.bicep` and deployed by the same
`az stack sub create` as everything else — fully automated, verified by
reading them back with `az role assignment list --scope <vault-id>` and
`--scope <namespace-id>` after the Day 25 deploy (both show exactly one
assignment, to `id-capstone-dev-api`, with the role names above).

### SQL: what's automatable, and what genuinely isn't

`modules/sql.bicep`'s `entraAdmin` resource
(`Microsoft.Sql/servers/administrators`) *is* fully declarable in Bicep and
was deployed automatically — it designates who is authorized to connect and
manage database-level permissions. It does **not**, by itself, grant the
managed identity any database access.

The actual grant — creating a database user for the identity and adding it to
roles — is T-SQL, run *inside* the database, and there is no ARM/Bicep
resource type for it. It can only be executed over a connection authenticated
**as the Entra administrator** designated above, which means it cannot be run
by this session's own `az` login (a different principal) and cannot be
scripted around. This was done by hand, in the Azure Portal's Query Editor,
authenticated as the Entra admin:

```sql
CREATE USER [id-capstone-dev-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-capstone-dev-api];
ALTER ROLE db_datawriter ADD MEMBER [id-capstone-dev-api];
```

Independently verified, in the same Query Editor session, with:

```sql
SELECT dp.name AS role_name, mp.name AS member_name
FROM sys.database_role_members drm
JOIN sys.database_principals dp ON drm.role_principal_id = dp.principal_id
JOIN sys.database_principals mp ON drm.member_principal_id = mp.principal_id
WHERE mp.name = 'id-capstone-dev-api';
```

which returned exactly two rows: `db_datareader | id-capstone-dev-api` and
`db_datawriter | id-capstone-dev-api`. This is the one step in Day 25 that a
human had to perform directly — not automated, not worked around.

### What the API's connection string actually looks like

`modules/api.bicep` builds this at deploy time, from module outputs only —
never a literal in any committed file:

```
Server=sql-capstone-dev-2fqdji.database.windows.net;Authentication=Active Directory Managed Identity;Encrypt=True;User Id=1b8d1172-ac6a-4c69-83c1-5729904bc38c;Database=sqldb-capstone-dev
```

`User Id` here is the identity's **client id** (not a username/password pair)
— required by Microsoft.Data.SqlClient's `Authentication=Active Directory
Managed Identity` mode specifically when a Web App has more than one
identity attached (it doesn't here, but the syntax is the same either way).
There is no password field in this connection string at all — the driver
fetches its own token from the platform's identity endpoint at connection
time, the same mechanism proven directly in
`output/identity-connectivity-proof.txt`.

### The Key Vault reference, and proof it resolves

`modules/keyvault.bicep` creates one demo secret,
`DemoConfig--SampleSetting` (double-underscore-shaped so App Service maps it
to `DemoConfig:SampleSetting` in .NET configuration, matching the `__` app
setting convention already used for `ConnectionStrings__CapstoneDb`). It
exists to prove the reference mechanism end-to-end for the day a real
secret (a third-party API key, say) needs one — this scaffold's SQL and
Service Bus config no longer has anything secret left to put in a vault at
all, now that both are identity-based.

`modules/api.bicep`'s `DemoConfig__SampleSetting` app setting is:

```
@Microsoft.KeyVault(SecretUri=https://kv-capstone-dev-klzi66.vault.azure.net/secrets/DemoConfig--SampleSetting)
```

— a pointer, not a value. `keyVaultReferenceIdentity` on the Web App is set
to the user-assigned identity's resource id explicitly (`modules/api.bicep`),
because Key Vault references default to a system-assigned identity if this is
unset, and this app has none to fall back to — confirmed live against
Microsoft Learn's Key Vault references article on 2026-09-09.

Proof it actually resolves (not just that the syntax is accepted) —
`az rest`, calling the Web App's own `configreferences/appsettings` endpoint
directly:

```bash
az rest --method get \
  --uri "https://management.azure.com/subscriptions/<SUBSCRIPTION_ID>/resourceGroups/rg-capstone-dev/providers/Microsoft.Web/sites/capstone-dev-api-f7nsoj/config/configreferences/appsettings?api-version=2026-07-15"
```

returned `"DemoConfig__SampleSetting": { "status": "Resolved", ... }` — the
platform genuinely fetched the secret value from the vault using the
identity's Key Vault Secrets User grant, not just accepted the reference
syntax without checking it.

### What zero secrets actually looks like here

All four app settings on the Web App, read back live with `az webapp config
appsettings list`, after this day's deploy:

| App setting | Value shape | Why this is safe to read back in plain text |
|---|---|---|
| `AZURE_CLIENT_ID` | a GUID (the identity's client id) | An identifier, not a credential — it tells the Service Bus SDK's `DefaultAzureCredential` *which* attached identity to use; it grants nothing by itself. |
| `ConnectionStrings__CapstoneDb` | `Server=...;Authentication=Active Directory Managed Identity;...;User Id=<clientId>;Database=...` | No password, no key, no token. `Authentication=Active Directory Managed Identity` is an instruction to the driver to go fetch its own short-lived token at connection time — the string itself authorizes nothing on its own. |
| `ServiceBus__FullyQualifiedNamespace` | a hostname | Just a DNS name. `disableLocalAuth: true` (set in `modules/servicebus.bicep` this day) means there is no SAS key this hostname could be paired with even if someone tried. |
| `DemoConfig__SampleSetting` | `@Microsoft.KeyVault(SecretUri=...)` | The app setting's own text is a pointer, not the secret — the real value lives only in Key Vault and is resolved by the platform at runtime using the identity's grant, per the proof above. |

Every one of the four is either a plain non-secret identifier/hostname, an
identity-based connection with no credential, or a Key Vault reference — no
plaintext secret exists in this Web App's configuration.

### Redeploy / reverify sequence

From `capstone/infra/`, with the same environment variables as before plus the
three new ones:

```bash
export CAPSTONE_SQL_ADMIN_LOGIN='...'
export CAPSTONE_SQL_ADMIN_PASSWORD='...'
export CAPSTONE_ENTRA_ADMIN_OBJECT_ID="<the Entra admin's object id>"
export CAPSTONE_ENTRA_ADMIN_LOGIN="<the Entra admin's UPN/display name>"
export CAPSTONE_KV_DEMO_SECRET_VALUE='any non-secret demo string'

az stack sub create \
  --name capstone-dev --location centralindia \
  --template-file main.bicep --parameters parameters/dev.bicepparam \
  --action-on-unmanage deleteAll --deny-settings-mode denyDelete \
  --deny-settings-apply-to-child-scopes \
  --description "Capstone dev environment - API host, SQL, Service Bus, identity, Key Vault"
```

Re-running this is idempotent for everything ARM-managed (identity, role
assignments, the Entra admin designation, Key Vault, app settings). It is
**not** idempotent for the manual SQL grant — that survives a Web App or even
a SQL server redeploy untouched (it's stored in the database itself), but if
the database were ever dropped and recreated, the `CREATE USER`/`ALTER ROLE`
statements above would need to be run again by a human, the same way.

### What's running right now, and its cost

Unlike Day 24 (fully torn down at the end of that session), `rg-capstone-dev`
was deliberately left running after Day 25 — Week 5 continues on this same
infrastructure. Its 16 resources (API host, SQL server + database, Service
Bus namespace + 2 topics, the managed identity, Key Vault + 1 secret, the
Entra admin designation, and 3 role assignments) cost roughly **$0.75-0.80
USD/day** while idle: App Service B1 ≈ $0.43/day, Service Bus Standard's flat
base charge ≈ $0.33/day, SQL serverless dev covered by the free monthly limit
≈ $0/day, Key Vault operations negligible — verified live against the [Azure
Retail Prices API](https://prices.azure.com/api/retail/prices) for Central
India on 2026-09-09, not guessed.

Tear down when Week 5 no longer needs it (same command as Day 24's, since
this is still the same Deployment Stack):

```bash
az stack sub delete --name capstone-dev --action-on-unmanage deleteAll --yes
```

**Day 26 update:** the stack now manages 26 resources. The new second Web App
(the trace-demo worker) adds **$0/day** — it shares Day 25's existing B1 plan,
which is billed for the plan's compute, not per app hosted on it. Log
Analytics/App Insights ingestion is billed per GB; this session's entire
traffic-generation exercise ingested **2.06 MB** (`Usage | summarize
sum(Quantity)`, run live) — a rounding error against both the 1 GB/day cap and
Azure Monitor's standard 5 GB/month free-per-workspace allowance, so this
addition's real incremental cost today is effectively **$0**. The same
`az stack sub delete` command above tears down everything, including the new
resources - the stack owns all of it.

### Honest limits, even after Day 25

- **No private networking.** Key Vault and Service Bus both still allow public
  network access (same gap already named for SQL above) — a production setup
  needs private endpoints on all three plus network restrictions on the Web
  App's outbound path.
- **No secret rotation story.** The one demo secret in Key Vault has no
  rotation policy or expiry set. A real secret would need one.
- **No break-glass access.** If `id-capstone-dev-api` were ever deleted, the
  Web App loses its only way to reach SQL, Service Bus, and Key Vault
  simultaneously — there is no second identity or fallback credential. A
  production setup would document (and probably automate) a recovery path:
  recreate the identity, re-run the role assignments (automatable) and the
  manual SQL grant (not), then reattach it to the Web App.
- **The SQL Entra admin is one human.** There's no Entra admin *group*
  designated (which Azure SQL also supports) — a single-person admin is a
  single point of failure for ever running another manual grant like this
  one.
- **No proof of a live TDS query against SQL from this session** — see
  `output/identity-connectivity-proof.txt`'s own "Honest limits" section for
  why, and what was verified instead. **Closed on Day 26** — see below;
  `/demo/db-ping` executes a real `SELECT 1` over this exact identity-based
  connection, live, from the deployed app.

## Observability (Day 26)

Wiring OpenTelemetry into the API and a new trace-demo worker, exporting to a
new workspace-based Application Insights resource, plus the KQL that reads it
back and an error-rate alert declared in Bicep. This section is the "prove it"
companion to `modules/monitoring.bicep`, `modules/alerting.bicep`,
`modules/worker.bicep`, and the OpenTelemetry wiring in
`host/Capstone.Web/Program.cs` and `host/Capstone.Worker/` — every claim below
has a live command, a captured query result, or an entry in
`VERIFICATION-LOG.md`'s Day 26 section backing it.

### What OpenTelemetry is, and how it relates to Application Insights

OpenTelemetry (OTel) is a vendor-neutral API and SDK for producing traces,
metrics, and logs — instrument code once, against OTel's interfaces, and the
telemetry can be exported anywhere an exporter exists, not locked to one
vendor's SDK. Application Insights is the *destination* this project chose:
`Azure.Monitor.OpenTelemetry.AspNetCore` (the API) and
`Azure.Monitor.OpenTelemetry.Exporter` (the worker, a plain ASP.NET Core app
with no ASP.NET-Core-specific distro to hang off) are exporters that translate
OTel's data model into Application Insights' schema (`requests`,
`dependencies`, `traces`, `exceptions`) and ship it to the workspace-based App
Insights resource `modules/monitoring.bicep` declares. The instrumentation
code (`AddSqlClientInstrumentation()`, `AddSource("Azure.Messaging.ServiceBus.*")`,
ASP.NET Core's own auto-instrumentation) is OTel; the destination and its
portal/KQL experience are Application Insights. Swapping the exporter later
(to a different backend, or to raw OTLP) would not require re-instrumenting
the application — that's the entire point of the vendor-neutral layer.

### Traces vs. metrics vs. logs — three different tools, not three names for the same thing

- **Traces** answer "what happened, across which hops, in what order, and how
  long did each part take?" — a tree of spans sharing one trace id. This is
  the right tool for a *specific* request's story: the distributed trace in
  §"the KQL, run for real" below is a trace question.
- **Metrics** answer "how is the system behaving in aggregate, over time?" —
  pre-aggregated numbers (request count, duration histograms) cheap to store
  and query even at high volume, but with no per-request detail. The right
  tool for a dashboard tile ("p99 latency this hour"), not for debugging one
  slow request.
- **Logs** answer "what did the application itself say happened, in its own
  words?" — free-form, structured records (`ILogger` calls in this project's
  code) that carry context traces and metrics can't: a specific SQL error
  message, a business-logic decision, a value that mattered. Correlated to
  trace/span ids (see next section) so a log line can be traced back to the
  request that produced it.

All three are wired here (`UseAzureMonitor()` and the worker's three
`Add*Exporter` calls each cover one), because each answers a different
question a real latency or error investigation actually asks.

### How trace context propagates — especially across Service Bus, and what breaks it

Within one process, propagation is automatic: .NET's `Activity` API tracks a
current activity per async-flow context, and every instrumentation library
(ASP.NET Core, HttpClient, SqlClient) creates its span as a child of whatever
is currently active — no code required. Across an HTTP call between two of
*your own* services, propagation is still automatic, carried in the
`traceparent` HTTP header both ends already understand.

**Across a message broker, none of that exists.** Service Bus has no concept
of a parent span; a message is just bytes plus a property bag. Whatever the
sender doesn't explicitly put *into* that property bag is gone by the time
the receiver's callback runs, no matter how well-instrumented either side is
in isolation — this is exactly what this task's own instructions called out
as the most common silent failure mode, and it is genuinely easy to get wrong
in two ways:

1. **The Azure SDK's own distributed tracing is off by default for messaging
   libraries.** `Azure.Experimental.EnableActivitySource` must be set
   (`AppContext.SetSwitch`) *before* any Service Bus client is constructed,
   in *both* processes — confirmed live via Azure SDK's own diagnostics
   documentation on 2026-09-10; most other, non-messaging Azure SDK libraries
   have this on by default, which makes it an easy trap to assume applies
   uniformly. Skipping this doesn't error — it just means no Service Bus
   spans ever appear, silently.
2. **Even with that switch on, nothing forces the parent context into the
   message unless code does it explicitly.** This project doesn't rely on
   the SDK's own automatic behavior for the logical "process this message"
   operation — `host/Capstone.Web/Program.cs`'s `/demo/trace-worker` endpoint
   writes `Activity.Current?.Id` (the W3C `traceparent` string) into
   `ServiceBusMessage.ApplicationProperties["traceparent"]` explicitly, and
   `host/Capstone.Worker/TraceDemoWorker.cs` reads it back with
   `ActivityContext.TryParse` and starts its own span as an explicit child.
   See `VERIFICATION-LOG.md` §8 for the real, captured proof this works: one
   trace id, correctly parent-chained, across two distinct service names.

What would break this in a real system: forgetting the `traceparent` property
in a NEW message type (each message shape needs its own propagation code —
there is no framework-level guarantee); a message crossing a boundary that
strips custom application properties (some routing/relay configurations do);
or a consumer that reads the message but never bothers to parse the property
back into an `ActivityContext` — the trace would then just start over from
that point, silently disconnected from its true origin.

### Sampling — a cost/visibility trade-off, not a free win

Sampling drops a fraction of traces before they're ever exported, cutting
ingestion cost roughly proportional to the fraction dropped. The trade-off:
a sampled-out trace is gone *entirely* — if it happened to be the one request
that hit a rare, low-frequency bug, that evidence is lost, not just
"aggregated away" the way a dropped metric point might be. This project sets
`OTEL_SAMPLING_RATIO=1.0` (no sampling) for dev via `otelSamplingRatio` in
`parameters/dev.bicepparam`, deliberately: at this project's dev traffic
volumes, sampling anything less than 100% would have thrown away a real
fraction of the ~200 requests this task's own traffic generation produced,
undermining the very percentile and error-rate queries the task asks for.
`parameters/prod.bicepparam` sets `0.25` — real production volume justifies
trading some completeness for cost, which dev volume does not. The value
flows from Bicep to both the API and worker as the `OTEL_SAMPLING_RATIO` app
setting, read in `Program.cs` and passed to `UseAzureMonitor`'s
`SamplingRatio` (API) or a `TraceIdRatioBasedSampler` (worker, since the raw
exporter package has no built-in Application-Insights-aware sampler the way
the ASP.NET Core distro does).

### What the ingestion cap protects against

App Insights and Log Analytics bill on data ingested and retained — an
uncapped resource is a real, if usually small, way to burn a subscription's
credit unnoticed (a bug that logs in a tight loop, a runaway retry). This
project sets a **1 GB/day** cap on *both* the Log Analytics workspace
(`workspaceCapping.dailyQuotaGb`) and the Application Insights component
(`currentbillingfeatures`/`DataVolumeCap`) — confirmed live
(`https://learn.microsoft.com/en-us/azure/azure-monitor/logs/daily-cap`,
2026-09-10) that for a workspace-based resource "the effective daily cap is
the minimum of the two settings," so setting only one is a documented gap,
not a safe simplification. 1 GB/day gives generous headroom above the
low-hundreds-of-KB a deliberate traffic-generation session actually produces
(see "the KQL, run for real" below for the real volume), while still
bounding a genuinely runaway scenario to a small, non-alarming fraction of
this subscription's remaining Free Trial credit. A warning fires at 80% of
the cap, before collection actually stops.

### Every KQL query, and what question it answers

All four live in `capstone/observability/queries/` (reusable `.kql` files,
not just pasted into this doc) with real, captured results in
`capstone/observability/results/`:

| Query | Question it answers |
|---|---|
| `01-latency-p50-p99-by-endpoint.kql` | For each endpoint, what's the typical (p50) and tail (p99) latency? A real complaint is almost always about the tail, not the median. |
| `02-dependency-breakdown.kql` | When a request is slow, where does the time actually go — SQL, Service Bus, or an outbound HTTP call (here, the managed identity token endpoint)? |
| `03-error-rate-by-endpoint.kql` | Is a given endpoint's failure rate rising, flat, or a one-off blip, bucketed in the same 5-minute granularity the alert evaluates on? |
| `04-distributed-trace-api-worker-db.kql` | For one specific request, what happened across every process it touched, and does it genuinely share one trace id with correct parent/child nesting? |

Real results and what they show are in the submission file
(`capstone/submission-day-26-task-1.md`) and `observability/results/*.json`.

### The alert: threshold, window, and why

Declared in `modules/alerting.bicep` as a log-based `scheduledQueryRules`
resource (a KQL alert, not a classic metric alert) — deliberately, so the
alert condition and the diagnostic query in
`03-error-rate-by-endpoint.kql` are the same query language, not two
different systems to reason about when one fires. Overall API error rate
(not split by endpoint, unlike the diagnostic query) — kept simple; per-
endpoint alerting would need per-endpoint thresholds this dev environment has
no real basis for setting yet.

- **Threshold: 30%.** High enough that a handful of the deliberate 404s this
  task's own traffic generation produces (a realistic amount of background
  noise for a dev environment) never falsely trips it, but low enough to
  catch a genuine, sustained problem quickly.
- **Minimum volume: 5 requests** in the window, before the rate is even
  computed — without this, one failed request out of one total reads as a
  meaningless "100% error rate" in a quiet dev environment.
- **Window: 15 minutes, evaluated every 5 minutes.** Wide enough that a
  handful of scattered dev-session requests land in one window together;
  narrow enough to notice a real problem within roughly one evaluation cycle.
- **Severity: 2 (Warning).** Not 0/1 — this is a dev environment demonstrating
  tracing, not a production system where a real person is paged. Sev 0/1 is
  the honest choice once this alert protects something real.
- **`skipQueryValidation: true`** — see `VERIFICATION-LOG.md` §2 for why this
  is required, not just convenient, on a freshly-created workspace.

### The worker: what it is, and what it explicitly is not

`host/Capstone.Worker` and its Bicep (`modules/worker.bicep`) exist **solely
to demonstrate distributed tracing across a message broker** — to give the
"API → worker → DB" trace something real to stitch across, and finally give
Service Bus (provisioned since Day 23, unused until today) a genuine
consumer. **It is not the InvoiceApproved integration event or supplier
notification flow `capstone/DESIGN.md` names** — those remain exactly as
undeveloped as they were through Day 25; that real build work belongs to
Days 29-31, per this task's own scope instructions. The worker's message
payload, its one demo table (`dbo.TraceDemoEvents`), and its topic choice
(`supplier-notifications`, picked arbitrarily) carry no meaning about which
real async flow is "closer" to being built.

It also isn't hosted the way originally planned — see
`VERIFICATION-LOG.md` §§4-6 for the real WebJobs-on-Linux dead end, the DI
bug that followed, and the deployment-tooling lesson learned pivoting to a
second Web App on the same plan and identity.

### Honest limits, even after Day 26

- **Single-environment, low-volume, artificial traffic.** Every number in
  this task's KQL results comes from ~200 requests generated by hand in one
  session, not real usage. Percentiles and error rates over that volume are
  illustrative, not statistically meaningful the way a week of real
  production traffic would be.
- **No availability tests, SLOs, or dashboards.** This task wires the raw
  telemetry and a handful of ad-hoc queries; a real production setup needs
  synthetic availability monitoring, defined SLO targets with error budgets,
  and a saved workbook/dashboard rather than queries run by hand each time.
- **No log-based metrics.** Every KQL query here runs on demand; nothing is
  pre-aggregated into a metric for cheap, high-frequency dashboarding.
- **No retention policy decision beyond the default.** 30 days (dev) is
  the free-included window, chosen for cost, not because a real incident
  investigation's actual lookback need was analyzed.
- **The alert has one action (email) and was verified to fire against a
  real, deliberately-triggered failure burst** — see the submission file for
  the exact evidence and timing; it has not been observed firing from
  organic, unplanned traffic, because none exists yet.
- **Sampling is 100% only because dev volume is trivially small.** The
  moment real traffic volume arrives, this ratio needs revisiting — it was
  never meant to be a permanent value, just the honest choice for this
  volume.
- **The Service Bus Data Receiver grant, like the Sender grant before it, is
  scoped to one subscription** — correct for this one demo consumer, but a
  second real consumer would need its own scoped grant, not a broadened one.

## Repository layout convention

`capstone/` is the **live** project — every day's development happens here,
edited in place, never copied for local work. `day-N/task-M/` folders
elsewhere in this repository are **frozen, end-of-day snapshots** of
`capstone/` as it stood when that day's work finished (e.g. `day-24/task-1/`
for this day) — committed once, on that day's branch, for a mentor to browse
that day's state without needing to check out a branch or reconstruct history.
Development never happens inside a snapshot; every snapshot is a copy made
*after* the real work in `capstone/` is done, and the two are verified
byte-identical (excluding build output, local Azure/azd state, and anything
secret) before committing.
