# Capstone infrastructure (Day 23 Task 1 — Bicep IaC)

Bicep templates for the capstone's API host, SQL database, and Service Bus
namespace. Lives at `capstone/infra/`, not a `day-23/task-1` folder — see
"Why this lives here" below.

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
- **Deployment stacks.** This is a plain `az deployment sub create`, not an
  [Azure Deployment Stack](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deployment-stacks)
  — no `deny-settings` protecting these resources from out-of-band changes, and no
  built-in "delete everything this stack ever created" semantics beyond the
  resource-group-scoped teardown above (which works here only because everything
  happens to live in one dedicated resource group).
- **CI-driven deployment.** These commands are run by hand from a developer
  machine. A production pipeline would run `what-if` on every pull request and
  `deploy` only from a protected branch, with the SQL credential coming from a
  pipeline secret store, not a locally exported environment variable.

See `VERIFICATION-LOG.md` for what was actually checked while building this,
including one real Azure `what-if` fidelity quirk found (not a bug in these
templates) and how it was confirmed harmless.
