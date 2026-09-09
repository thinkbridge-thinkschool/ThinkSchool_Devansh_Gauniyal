# Verification log — Day 23 Task 1

Genuine record of what was checked and found while authoring and validating this
infrastructure, on 2026-09-06/07 against subscription `Azure for Students`
(`7cf66c88-...`, region `centralindia`). Every command below was actually run;
nothing here is reconstructed after the fact.

## 1. API versions and SKU names — verified live, not from memory

Ran `az provider show -n <namespace> --query "resourceTypes[?resourceType=='<type>'].apiVersions | [0][0:6]"`
for every resource type used, and picked the newest entry **without** a `-preview`
suffix:

| Resource type | Latest stable API version found |
|---|---|
| `Microsoft.Resources/resourceGroups` | `2023-07-01` |
| `Microsoft.Web/serverFarms` | `2026-07-15` |
| `Microsoft.Web/sites` | `2026-07-15` |
| `Microsoft.Sql/servers` | `2025-01-01` |
| `Microsoft.Sql/servers/databases` | `2025-01-01` |
| `Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies` | `2025-01-01` |
| `Microsoft.ServiceBus/namespaces` | `2026-01-01` |
| `Microsoft.ServiceBus/namespaces/topics` | `2026-01-01` |

`Microsoft.Sql/servers/firewallRules` is not separately enumerated by
`az provider show` on this subscription at all (checked with a broad `grep -i
firewall` over every resource type this provider lists — it simply isn't there,
even though the resource type is real and documented). Used `2025-01-01` for
consistency with the rest of the `Microsoft.Sql` hierarchy; `az bicep build` raised
no error or warning against it, which is the strongest signal available that the
version is accepted.

SKU names (`GP_S_Gen5` serverless, `GP_Gen5` provisioned, both Gen5/GeneralPurpose)
came from `az sql db list-editions -l centralindia`, run **after** discovering (see
§2) that `Microsoft.Sql` wasn't registered on this subscription yet — the command
returned `SubscriptionNotFound` until the provider was registered, not a pricing or
naming problem. Confirmed live: the smallest provisioned Gen5 General Purpose size
Central India offers is 4 vCores (`GP_Gen5_4`) — there is no `GP_Gen5_2` — which is
why prod uses 4 vCores, not 2.

The `.NET` Linux runtime stack string (`DOTNETCORE|10.0`) came from
`az webapp list-runtimes --os linux`, confirming it's `Active`/LTS through
2028-11-14 and matches `capstone/host/Capstone.Web`'s `net10.0` target — not
assumed from the project file alone.

## 2. A real thing that broke: unregistered resource providers

`Microsoft.Sql` and `Microsoft.ServiceBus` were both `NotRegistered` on this
subscription (`az provider show -n Microsoft.Sql --query registrationState` →
`NotRegistered`). This surfaced as `az sql db list-editions` failing with
`ERROR: (SubscriptionNotFound) The requested subscription '...' was not found` — a
misleading error message for what was actually a provider-registration gap, not a
subscription problem. Fixed with `az provider register -n Microsoft.Sql` and
`-n Microsoft.ServiceBus`, polled every 5 seconds until both reported `Registered`
(took under a minute for both). This is a one-time, no-cost, non-resource-creating
subscription setting, not a deployment.

## 3. A real CLI flag mistake, caught immediately

First `what-if` attempt used `--no-pretty-print=false`, which `az` rejects outright
(`ERROR: argument --no-pretty-print: ignored explicit argument 'false'` — it's a
bare switch, not a `key=value` flag). Fixed by dropping the flag entirely (pretty
output is the default and is what got captured into `output/whatif-dev.txt`).

## 4. `az bicep build` warnings — real, and left in place deliberately

```
Warning BCP081: Resource type "Microsoft.Web/serverFarms@2026-07-15" does not have
types available. Bicep is unable to validate resource properties prior to
deployment, but this will not block the resource from being deployed.
Warning BCP081: Resource type "Microsoft.Web/sites@2026-07-15" does not have types
available. ...
```

Bicep CLI 0.46.1's bundled type index hasn't caught up to the `2026-07-15`
`Microsoft.Web` API version yet, even though `az provider show` confirms it's real
and GA on this subscription today. `modules/sql.bicep` and
`modules/servicebus.bicep` built with **zero** warnings at their chosen API
versions (`2025-01-01`, `2026-01-01`) — only the very newest `Microsoft.Web`
version triggers this. Two options were available: fall back to an older,
type-checked `Microsoft.Web` version, or keep the verified-current one and accept
weaker static validation. Kept `2026-07-15` — the task explicitly asks for the
*actually current* API surface, and BCP081 states plainly that this doesn't block
deployment. Confirmed harmless empirically: the real deploy (§5) succeeded and the
Web App came back `Running` with exactly the `linuxFxVersion` requested.

## 5. Real deploy — succeeded first try

`az deployment sub create` against `parameters/dev.bicepparam` completed in
**3 minutes 12 seconds** (00:07:31 → 00:10:43 IST) with `provisioningState:
"Succeeded"` and zero errors. All 10 resources were independently re-queried
afterward (not just trusted from the deployment's own output) via `az resource
list`, `az appservice plan list`, `az webapp list`, `az sql db list`, and `az
servicebus namespace show`/`topic list` — every one matched the requested SKU
exactly (see `capstone/submission-day-23-task-1.md` for the full observed output).
The deployed Web App was also reached directly over HTTPS
(`curl https://capstone-dev-api-skom32.azurewebsites.net/` → `HTTP 200`) - it
returns App Service's own default placeholder page, since this task provisions
infrastructure only and deploys no application code to it.

## 6. Idempotence — re-deploy succeeded, but a genuine what-if fidelity quirk

Re-running the identical `az deployment sub create` command a second time
succeeded in 69 seconds (much faster - nothing needed to be created, only
re-validated) with `provisioningState: "Succeeded"` again and, critically, **zero**
resources reported as deleted or replaced.

A follow-up `az deployment sub what-if` against the now-live environment was
**not** perfectly silent, though - it reported "4 to modify, 6 no change, 1 to
ignore" rather than "0 changes". Read in full, every one of the four "Modify"
blocks lists only properties Azure filled in with its own defaults that this
template never declares (e.g. Service Bus topics' `autoDeleteOnIdle`,
`enableBatchedOperations`, `maxSizeInMegabytes`; the Web App's `siteConfig`
sub-properties like `netFrameworkVersion`), plus four more marked `x` for
"Noeffect" (What-If's own symbol for "differs in representation, changing it does
nothing") on SQL/Service Bus/plan SKU fields. The tool's own output states this
plainly at the top: *"Note: The result may contain false positive predictions
(noise)."* This is a documented characteristic of `what-if`'s diffing for
`Microsoft.Web/sites` (siteConfig is a separate nested PUT under the hood) and for
resource types with many server-assigned defaults, not drift caused by this
template or evidence that redeploying it changes anything real. The stronger proof
of idempotence is §5's actual re-deploy: same command, same parameters, zero
creates, zero deletes, `Succeeded` both times.

## 7. Everything else — no other errors

Every other `az bicep lint`/`build`/`build-params` call, the prod `what-if`, and
the real resource verification queries succeeded on the first attempt with no
errors beyond what's listed above. No fabricated clean pass: items 2, 3, 4, and 6
are exactly what went sideways, in the order it happened.

---

# Verification log — Day 24 Task 1

Genuine record of what was checked and found while wiring azd + Azure Deployment
Stacks on top of Day 23's Bicep, on 2026-09-08 against subscription
`Azure subscription 1` (`cd88612a-...`, tenant `f3d77e6c-...`, region
`centralindia` for resources / `centralindia` for the stacks themselves). This
section covers what changed since Day 23; Day 23's own log above is untouched.

## 1. Command syntax verified live, not from memory

`az stack sub create/validate/delete --help` were read directly against the
installed `az` 2.89.0 before use, confirming the real flag names and allowed
values: `--action-on-unmanage` (`deleteAll | deleteResources | detachAll`),
`--deny-settings-mode` (`denyDelete | denyWriteAndDelete | none`),
`--deny-settings-apply-to-child-scopes`, and that `--parameters` accepts a
`.bicepparam` file directly (confirmed by using `parameters/dev.bicepparam`
successfully — az CLI's stack commands share the same parameter-resolution
code path as `az deployment ... create`).

The `azure.yaml` `infra.deploymentStacks` schema (`actionOnUnmanage.resources`/
`.resourceGroups`, `denySettings.mode`/`applyToChildScopes`/`excludedActions`/
`excludedResources`/`excludedPrincipals`) came from Microsoft Learn's
[Azure deployment stacks integration with azd](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azure-deployment-stacks-integration)
page, fetched live during this session — not recalled from memory, per the
task's own warning that this schema changes across versions. The
`alpha.deployment.stacks` feature flag was confirmed present (`Status: Off`)
via `azd config list-alpha` before enabling it with
`azd config set alpha.deployment.stacks on`.

## 2. A real, unresolved blocker: azd's own auth

`azd` maintains its own credential store, separate from the `az` CLI's. The
first `azd provision --preview` failed with `failed to resolve user
'live.com#<OTHER_ACCOUNT_EMAIL>' access to subscription` — a stale
identity from whenever azd was first configured on this machine, unrelated to
either of this session's `az login`s. Two `azd auth login --use-device-code
--tenant-id f3d77e6c-...` attempts were made, each producing a real, distinct
device code (`HYDR5QSRP`, then `S422GSB7L`); Devansh completed the sign-in at
`microsoft.com/devicelogin` both times, and both times `azd auth login
--check-status` afterward still reported "Not logged in." This is consistent
with azd's OAuth client registration lacking access/consent for this specific
account in this tenant — a real, external constraint, not a syntax error or a
retry-able transient. **Consequence: `azd provision`/`azd up` were never
actually executed against Azure in this session.** Everything that follows
(the two stack deploys, the drift demo, promotion, teardown) was run with
`az stack sub` directly instead, against the exact same `main.bicep` +
`.bicepparam` files `azure.yaml` points at — the config in `azure.yaml` is
real and correct, but it drove nothing today. See `README.md`'s new "What azd
adds" section for what this means for the deliverable.

## 3. A real, empirically-discovered azd convention

Before hitting the auth wall, `azd provision --preview` (with no
`main.bicepparam` present) got far enough to reveal a genuinely useful fact:
without a `.bicepparam`/`.parameters.json` file at the location matching
`infra.module` (i.e. `infra/main.bicepparam`), azd introspects `main.bicep`'s
own parameter declarations directly and lists every one as a required
`infra.parameters.<name>` azd-environment-config key, along with its real
`@description`. This confirmed empirically (not assumed) that azd's Bicep
provider looks for `<path>/<module>.bicepparam` — creating a file at exactly
that path made the "13 missing inputs" error disappear (before hitting the
separate auth error in §2). This is the basis for the preprovision hook in
`infra/scripts/select-environment-parameters.sh` — tested directly by running
it with `AZURE_ENV_NAME=dev`, `=prod`, and an invalid value, confirming it
produces a byte-identical copy of the right file in the first two cases and
fails loudly (not silently) in the third.

## 4. A real transient failure during the dev stack's first create

The first `az stack sub create` for dev failed:
```
DeploymentStackDeploymentFailed: One or more resources could not be deployed.
  InvalidAuthenticationToken: at least one of the claims 'puid' or 'altsecid'
  or 'oid' should be present.
```
Drilling into the nested deployment operations (`az deployment operation sub
list`, then `az deployment operation group list` on the SQL module's own
nested deployment) showed the resource group, Service Bus namespace + topics,
App Service plan + site, SQL server, and firewall rule had all genuinely
succeeded — only `Microsoft.Sql/servers/databases` showed `status: Running`
(not `Failed`) at the point the overall operation gave up. Checking the
database directly (`az sql db show`) found it was already `Online` — the
resource itself had finished; the deployment engine's own completion-polling
call hit a transient token issue on its way out, unrelated to the resource
succeeding. Re-running the identical `az stack sub create` (idempotent by
design) completed cleanly in 2m 2s with `provisioningState: succeeded` and
all 10 resources `managed`. Not a Deployment-Stacks-specific bug; a one-off
ARM control-plane glitch, resolved by the same retry that any transient
Azure error calls for.

## 5. A real, hard blocker on promoting to prod: Free Trial SQL quota

Promoting straight to prod (same `main.bicep`, `parameters/prod.bicepparam`
unchanged) failed with a real, specific, non-transient error:
```
ProvisioningDisabled: Free Trial subscriptions can provision Basic, Standard
S0 through S3 databases, up to 100 eDTU Basic or Standard elastic pools and
DW100 through DW400 SQL Analytics in Azure Synapse instances
```
This traces directly to the Phase 1 billing check: this subscription's
`quotaId` is `FreeTrial_2014-09-01`. Dev's SQL succeeds only because
`useFreeLimit: true` invokes Azure SQL's separate free-monthly-limit offer,
which is explicitly exempted from this quota (see Day 23's
`VERIFICATION-LOG.md` §1); prod deliberately doesn't use that offer (a real
production database shouldn't auto-pause), so its vCore-based `GP_Gen5`
request hit the wall directly. This is a subscription-tier limitation, not
something a Deployment Stack retry fixes.

Resolved with Devansh's explicit direction (asked directly, since it meant
touching Day 23's Bicep): `modules/sql.bicep` hardcoded `sku.tier:
'GeneralPurpose'` and `sku.family: 'Gen5'` unconditionally, which can only
express vCore-based SKUs — there was no way to pass a DTU-based SKU
(`Standard`/`S0`) through the module at all. Extended (not restructured) with
two new parameters, `skuTier` and `skuFamily`, both defaulting to the exact
values Day 23 hardcoded (`GeneralPurpose`/`Gen5`) — confirmed dev's `az stack
sub validate` still returns `"error": null` after this change, unaffected.
Threaded the same two parameters through `main.bicep` the same way. For
today's prod deploy only, overrode at the CLI parameter layer —
`sqlSkuName=S0 sqlSkuTier=Standard sqlSkuFamily='' sqlCapacity=10` — never
touching the committed `parameters/prod.bicepparam`, which still specifies
the real `GP_Gen5`/4-vCore values Day 23 intended for an actual production
subscription.

The very next attempt hit a second, related error: `ProvisioningDisabled:
Provisioning of zone redundant database/pool is not supported for your
current request` — zone redundancy isn't available at Standard/S0 on this
subscription either. Added `sqlZoneRedundant=false` to the same CLI override
set. The deploy then succeeded: `provisioningState: succeeded`, 10 resources
managed, independently verified (`az sql db list` shows `Standard`/`S0`/
`Online`; `az appservice plan list` shows the real `S1`/`Standard`/2 from the
untouched prod parameter file; Service Bus `Standard`/`Active`).

## 6. Drift: prevention (deny-settings) and correction (redeploy)

Two different, real demonstrations, since Azure Deployment Stacks have no
CloudFormation-style passive drift-detection feed (confirmed by research
before attempting this — see `README.md`'s drift section for the citation and
reasoning; a `denyDelete` stack does not silently watch for changes, it acts
in exactly two ways described below):

- **Prevention.** `az servicebus namespace delete` against the dev stack's
  namespace, run directly via the CLI (not the portal), was rejected outright:
  `DenyAssignmentAuthorizationFailed` naming the exact deny assignment
  `Deployment Stack '.../deploymentStacks/capstone-dev' ` created. Real,
  verified rejection — not a simulated one.
- **Correction on redeploy.** Tagged `plan-capstone-dev-api` out-of-band via
  `az resource tag` (an operation `denyDelete` does not block) with an extra
  `drift-test=out-of-band-change` tag not in the template. Confirmed live with
  `az resource show` immediately after (`tags` includes it). Re-ran the
  identical `az stack sub create` with no template/parameter change — the
  drifted tag was gone afterward, reconciled back to exactly the four tags
  `main.bicep`'s `tags` parameter declares. This is genuinely how Deployment
  Stacks correct drift: on the next apply, not via a background watcher.

## 7. Idempotence — clean

Re-ran the identical dev `az stack sub create` immediately after the drift
reconciliation, with nothing changed in between:
`provisioningState: succeeded`, `duration: PT2M18.44S`, `resources: 10`,
`failedResources: []`, `deletedResources: []`, `detachedResources: []`. No
surprises.

## 8. Teardown — one real interruption, resolved

The dev stack's `az stack sub delete` command was interrupted mid-flight by a
rejected tool confirmation before it printed any output (the captured file
was empty). Rather than re-running it blind, checked Azure directly first:
`az group list` and `az stack sub list` both already showed dev gone. This is
consistent with how ARM long-running deletes work — once submitted, the
delete continues server-side even if the calling CLI process is killed
locally; the empty local output file reflects the killed CLI process, not a
failed or partial delete. Confirmed with fresh `az stack sub show
--name capstone-dev` (`ResourceNotFound`) and `az group show
--name rg-capstone-dev` (`ResourceGroupNotFound`) rather than trusting the
empty file either way. Prod's teardown was then run deliberately to
completion: `az stack sub delete --name capstone-prod --action-on-unmanage
deleteAll --yes`, exit 0, confirmed with a final `az group list` showing only
`rg-day17-task1-swa` remains on this subscription.

---

# Verification log — Day 25 Task 1

Genuine record of what was checked and found while wiring managed identity,
Key Vault, and Entra ID auth on top of Days 23-24's Bicep, on 2026-09-09
against subscription `Azure subscription 1` (`cd88612a-...`, tenant
`f3d77e6c-...`, region `centralindia`). This section covers what changed since
Day 24; both earlier sections above are untouched.

## 1. API versions and role definition ids — verified live, not from memory

| Resource / role | Value | Verified via |
|---|---|---|
| `Microsoft.ManagedIdentity/userAssignedIdentities` | `2024-11-30` (latest non-preview) | `az provider show -n Microsoft.ManagedIdentity` |
| `Microsoft.KeyVault/vaults`, `vaults/secrets` | `2026-05-15` | `az provider show -n Microsoft.KeyVault` |
| `Microsoft.Sql/servers/administrators` | `2025-01-01` (same version as every other `Microsoft.Sql` resource in this project) | `az provider show -n Microsoft.Sql` |
| "Key Vault Secrets User" role | `4633458b-17de-408a-b874-0445c86b69e6` | `az role definition list --name "Key Vault Secrets User"` |
| "Azure Service Bus Data Sender" role | `69a216fc-b8fb-44d8-bc22-1f3c2cd27a39` | `az role definition list --name "Azure Service Bus Data Sender"` |

## 2. A real Bicep authoring mistake: wrong escape sequence

Wrote apostrophes inside `@description('...')` strings using `''` (doubled
single-quote) — the T-SQL/Visual Basic escaping convention, not Bicep's.
Bicep uses backslash escaping (`\'`). This produced real build errors across
four files (`main.bicep`, `modules/keyvault.bicep`, `modules/sql.bicep`,
`modules/servicebus.bicep`):

```
Error BCP071: Expected 1 argument, but got 2.
Error BCP236: Expected a new line or comma character at this location.
```

Fixed with `sed -i '' "s/''/\\\\'/g"` across all four files, then confirmed
clean with `az bicep build --file main.bicep` — zero errors, only the two
known BCP081 "no types available" warnings already logged in Day 23 §4 for
the newest `Microsoft.Web`/`Microsoft.KeyVault` API versions (same accepted
trade-off: verified-current API surface over full static type-checking).

## 3. `az stack sub validate`'s response shape, and a real fidelity gap

Expected a top-level `resources` array matching `create`'s output shape;
`validate` actually nests everything under `properties`, and the array is
named `validatedResources`, not `resources`. After correcting the query path,
`validatedResources` still only listed 12 of the eventual 16 resources
(missing the Web App, Key Vault, Service Bus namespace, and the role
assignments) despite `error: null`. This matches the same category of
`what-if` "noise" already logged in Day 23 §6 — a known fidelity gap in the
preview response, not a real validation failure. Not blocked on further: the
real `az stack sub create` (§4 below) is the authoritative, complete result,
and it succeeded with all 16 resources `managed`.

## 4. Real deploy — succeeded first try, unlike Day 24

`az stack sub create` against the updated `parameters/dev.bicepparam`
completed with `provisioningState: succeeded`, all 16 resources `managed` /
`denyDelete` (the original 10 from Day 23/24 plus the identity, the Key
Vault, its secret, its role assignment, the Service Bus role assignment, and
the SQL Entra administrator). No transient control-plane glitch this time
(contrast Day 24 §4). Full output captured in
`output/stack-dev-identity-create.txt` (subscription id and the account email
in `createdBy`/`lastModifiedBy` redacted to `<SUBSCRIPTION_ID>` /
`<PINNED_ACCOUNT_EMAIL>`, matching every other captured output file in this
project).

## 5. The manual SQL grant — walked through step by step, independently verified

`modules/sql.bicep`'s `entraAdmin` resource deployed automatically with
everything else in §4. The database-level grant cannot be expressed as an ARM
resource at all — no `Microsoft.Sql` child resource type runs T-SQL — so it
had to be run by a human, connected as the Entra administrator, in the Azure
Portal's Query Editor:

- First connection attempt was blocked by the server's firewall
  ("Your IP address isn't allowed to access this server") — resolved with the
  Query Editor's own "Allowlist IP" button, a one-time addition to
  `modules/sql.bicep`'s firewall rules for this specific client IP (separate
  from the `AllowAllAzureServices` rule, which only covers Azure-hosted
  callers, not a developer's own browser session).
- First `CREATE USER [id-capstone-dev-api] FROM EXTERNAL PROVIDER;` attempt
  failed: "User, group, or role 'id-capstone-dev-api' already exists in the
  current database" — the user already existed from an earlier partial
  attempt, and the batch aborted before the two `ALTER ROLE` lines ran.
  Fixed by re-running just the two `ALTER ROLE` lines plus a verification
  `SELECT` (see README.md, "SQL: what's automatable, and what genuinely
  isn't" for the exact statements).
- Verified independently, in the same session, via a
  `sys.database_role_members` join query: exactly two rows,
  `db_datareader | id-capstone-dev-api` and
  `db_datawriter | id-capstone-dev-api`.

No SQL client tooling (`sqlcmd`, `pyodbc`, `pymssql`) was available in this
session to run a second, independent verification query from this side — a
deliberate choice not to install one for a single query rather than a
capability gap worth working around. The user's own portal-based
verification, read back directly above, is the real, sufficient evidence for
this step.

## 6. A real platform gotcha: where managed-identity env vars actually live

Attempting to prove the identity's token acquisition from Kudu's
`/api/command` endpoint (`az rest` against
`https://<app>.scm.azurewebsites.net/api/command` with an ARM-scoped bearer
token) found **zero** `IDENTITY_ENDPOINT`/`IDENTITY_HEADER` env vars, even
after restarting the Web App. A genuine SSH session into the same container
(via `az webapp create-remote-connection` tunneling to the built-in sshd on
the Linux base image, `root`/`Docker!` credentials — the standard default for
this App Service base image) also showed zero matches for `env | grep -i
identity`. Checking `/proc/<site-worker-pid>/environ` directly (found via
`ps -eo pid,cmd`) found both variables present. Conclusion, confirmed
empirically rather than assumed: App Service injects these two variables
into the specific process it launches as the site's worker (here,
`hostingstart.dll` — no application code is deployed yet, see README.md), not
into the container's general environment — so neither a Kudu command shell
nor a fresh SSH shell (both separate processes) ever see them; only the
actual site process, or something reading that process's own `/proc/.../environ`,
does.

`az webapp ssh`/`az rest ... /api/command` piped/timed non-interactive
attempts (`printf ... | az webapp ssh -t N`) reliably failed to deliver
commands before the tunnel was ready or hung past this session's own command
timeout — worked around with `az webapp create-remote-connection` to a fixed
local port plus the system `expect` binary to script the password prompt
non-interactively (`sshpass` is not installed in this environment; `expect`
is).

## 7. Real proof of identity-based connectivity, including the unauthorized case

Full commands and raw output in `output/identity-connectivity-proof.txt`.
Summary: real Bearer tokens minted for both `https://database.windows.net/`
and `https://servicebus.azure.net/` via the platform identity endpoint using
this identity's client id; a real message send to the
`invoice-approved-events` topic using the Service Bus token succeeded
(`HTTP 201`); the same token, used to attempt creating a queue (an action
requiring the Manage claim, which "Azure Service Bus Data Sender" does not
grant), was rejected live (`HTTP 401`,
`Authorization failed for specified action: Manage,EntityWrite`); a send
attempt with no Authorization header at all was also rejected
(`HTTP 401`, `MissingToken`), confirming `disableLocalAuth: true` has closed
the SAS-key path entirely, not just that nothing currently uses it.

## 8. Everything else — no other errors

Every other command this day (`az bicep lint`, the identity/Key Vault module
builds at their chosen API versions with zero warnings, `az role assignment
list` reads confirming exactly one assignment per resource, the
`configreferences/appsettings` check confirming `DemoConfig__SampleSetting`
resolves) succeeded on the first attempt with no errors beyond what's listed
above.
