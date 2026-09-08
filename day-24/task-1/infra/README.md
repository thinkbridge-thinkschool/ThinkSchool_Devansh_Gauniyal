# Capstone infrastructure (Day 23 Task 1 — Bicep IaC; Day 24 Task 1 — Deployment Stacks + azd)

Bicep templates for the capstone's API host, SQL database, and Service Bus
namespace, deployed as Azure Deployment Stacks and configured through azd.
Lives at `capstone/infra/`, not a `day-N/task-M` folder — see "Why this lives
here" below, and see "Repository layout convention" for how `day-N/task-M/`
snapshots relate to this live folder.

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
  wired.

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
- **Managed identity wiring.** The Web App has no identity assigned, and nothing
  authenticates to SQL or Service Bus via managed identity — the application would
  need `Microsoft.Web/sites`' `identity` block, SQL Entra-only auth, and Service Bus
  RBAC role assignments, none of which exist here.
- **Key Vault.** The SQL admin credential is supplied at deploy time via an
  environment variable, not stored anywhere Azure-side for the application to read
  later. A production setup would put it (or better, a managed-identity-only
  connection with no password at all) in Key Vault, referenced by the Web App via a
  Key Vault reference app setting.
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
- **Private networking.** The SQL firewall rule (`AllowAllAzureServices`,
  `0.0.0.0`–`0.0.0.0`) allows any Azure-hosted resource through, because the Web App
  has no static outbound IP without VNet integration. A production version needs
  VNet integration on the Web App plus a private endpoint on the SQL server, with
  public network access disabled entirely.
- **Managed identity wiring.** The Web App has no identity assigned, and nothing
  authenticates to SQL or Service Bus via managed identity — the application would
  need `Microsoft.Web/sites`' `identity` block, SQL Entra-only auth, and Service Bus
  RBAC role assignments, none of which exist here.
- **Key Vault.** The SQL admin credential is supplied at deploy time via an
  environment variable, not stored anywhere Azure-side for the application to read
  later. A production setup would put it (or better, a managed-identity-only
  connection with no password at all) in Key Vault, referenced by the Web App via a
  Key Vault reference app setting.

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
