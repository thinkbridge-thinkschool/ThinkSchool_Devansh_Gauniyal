# Day 24 Task 1 — Deployment Stacks + azd

## Notes for mentor

This continues from `capstone/submission-day-23-task-1.md`. Same reason as
every prior capstone day: lives at `capstone/infra/`, not a `day-24/task-1`
folder — a frozen, byte-identical snapshot of `capstone/` as it stands today
is committed separately at `day-24/task-1/` for browsing, but all development
happened in `capstone/`.

`azd` version used: **1.31.1**. Commit hash representing this day's state:
this same commit — see `git log -1` or the GitHub commit view (a commit
can't contain its own hash inside its own tree). Both stacks were fully
torn down after capturing evidence — nothing is still running or costing
money as of this commit.

**One line on what Deployment Stacks give you over plain deployments**: an
explicit, queryable ownership record of every resource a deploy created, plus
active protection (`denySettings`) against out-of-band deletion and one-command
clean teardown of everything the stack owns — none of which a plain
`az deployment create` gives you.

**Honest note on azd**: `azd`'s own auth (a separate credential store from
`az`) could not be completed this session — two real device-code attempts,
both confirmed done in the browser, both left `azd` reporting "Not logged in."
`azure.yaml` below is real and correctly configured (verified its
`deploymentStacks` schema live against Microsoft Learn, and empirically
confirmed its `.bicepparam` file-discovery convention), but the actual deploys
were run with `az stack sub` directly against the same template/parameter
files it points at. Full detail in `capstone/infra/VERIFICATION-LOG.md`, §2.

### The azd config (`capstone/azure.yaml`)

```yaml
name: capstone

infra:
  provider: bicep
  path: infra
  module: main
  deploymentStacks:
    actionOnUnmanage:
      resources: delete
      resourceGroups: delete
    denySettings:
      mode: denyDelete
      applyToChildScopes: true
      excludedActions: []
      excludedResources: []
      excludedPrincipals: []

hooks:
  preprovision:
    shell: sh
    run: ./infra/scripts/select-environment-parameters.sh
```

The `preprovision` hook (`capstone/infra/scripts/select-environment-parameters.sh`)
copies whichever of `parameters/dev.bicepparam` / `parameters/prod.bicepparam`
matches the active azd environment to `infra/main.bicepparam` — the filename
azd's Bicep provider looks for by convention. No parameter value is
duplicated anywhere; it only selects one of Day 23's two existing files.

### Real deploy output — dev

Command: `az stack sub create --name capstone-dev --location centralindia --template-file main.bicep --parameters parameters/dev.bicepparam --action-on-unmanage deleteAll --deny-settings-mode denyDelete --deny-settings-apply-to-child-scopes --yes`

- First attempt failed with a real, transient `InvalidAuthenticationToken`
  error during the SQL database's completion polling (the database itself had
  actually finished — confirmed `Online` via `az sql db show` even though the
  stack reported failure). Re-ran the identical command; succeeded.
- `provisioningState: "succeeded"`, 10 resources, all `"status": "managed"`,
  `"denyStatus": "denyDelete"`.
- Independently verified: App Service plan `B1`/`Basic`; SQL database
  `GP_S_Gen5`/`Online`; Service Bus namespace `Standard`/`Active` with both
  topics.
- Full raw output: `capstone/infra/output/stack-dev-create.txt` (subscription
  ID redacted to `<SUBSCRIPTION_ID>`).

### Real deploy output — prod

Command: `az stack sub create --name capstone-prod --location centralindia --template-file main.bicep --parameters parameters/prod.bicepparam sqlSkuName=S0 sqlSkuTier=Standard sqlSkuFamily='' sqlCapacity=10 sqlZoneRedundant=false --action-on-unmanage deleteAll --deny-settings-mode denyDelete --deny-settings-apply-to-child-scopes --yes`

- First attempt failed with a real, hard, non-transient error: `Free Trial
  subscriptions can provision Basic, Standard S0 through S3 databases...` —
  this subscription's `GP_Gen5` (vCore-based) quota is zero outside the
  free-limit offer dev uses, which prod deliberately doesn't use. Extended
  `modules/sql.bicep` (parameterized `sku.tier`/`sku.family`, previously
  hardcoded to `GeneralPurpose`/`Gen5`) so a DTU-based SKU could be expressed
  at all, then overrode `sqlSkuName`/`sqlSkuTier`/`sqlSkuFamily`/`sqlCapacity`
  at the CLI layer only — **the committed `parameters/prod.bicepparam` file is
  unchanged** and still specifies the real `GP_Gen5`/4-vCore/zone-redundant
  values a genuine production subscription would use.
- Second attempt failed too: `Provisioning of zone redundant database/pool is
  not supported for your current request` — added `sqlZoneRedundant=false` to
  the same CLI override set.
- Third attempt: `provisioningState: "succeeded"`, 10 resources managed.
  Independently verified: App Service plan `S1`/`Standard`/2 instances
  (unmodified, from the real prod parameter file); SQL database
  `Standard`/`S0`/`Online`; Service Bus `Standard`/`Active`.
- Full raw output: `capstone/infra/output/stack-prod-create.txt`.

### Drift-detection evidence

Azure Deployment Stacks have no CloudFormation-style passive drift feed
(confirmed by research before attempting this). Demonstrated the two real
mechanisms a stack does give you:

1. **Prevention** — ran `az servicebus namespace delete -g rg-capstone-dev -n
   sb-capstone-dev-tnjxju` (a real CLI call against a stack-managed resource,
   not the portal). Rejected:
   ```
   ERROR: (DenyAssignmentAuthorizationFailed) The client
   'live.com#<PINNED_ACCOUNT_EMAIL>' ... has permission to perform
   action 'Microsoft.ServiceBus/namespaces/delete' ... however, the access is
   denied because of the deny assignment ... created by Deployment Stack
   '.../deploymentStacks/capstone-dev'.
   ```
2. **Correction on redeploy** — tagged `plan-capstone-dev-api` out-of-band
   (`az resource tag ... drift-test=out-of-band-change`), confirmed the tag
   live via `az resource show`, then re-ran the identical `az stack sub
   create`. The `drift-test` tag was gone afterward — reconciled back to
   exactly the four tags the template declares. Full before/after tag output
   and the idempotence re-run (10 resources, 0 failed/deleted/detached) are in
   `capstone/infra/VERIFICATION-LOG.md`, §6–7.

### Teardown

Both stacks torn down: `az stack sub delete --name capstone-dev
--action-on-unmanage deleteAll --yes` and the same for `capstone-prod`.
Confirmed with a fresh `az group list` on the pinned subscription showing only
`rg-day17-task1-swa` remains (untouched throughout) — no `rg-capstone-dev`,
no `rg-capstone-prod`, `az stack sub list` returns empty.

## What did you learn this session?
Deployment Stacks don't watch for drift on their own the way I assumed — they only actually check when you either try to delete something they own (which gets blocked) or when you redeploy, which is when a changed property gets put back the way the template says it should be.
I also learned azd keeps its own separate login from the regular Azure CLI, so being logged into one doesn't mean the other works — that cost me the most time today.

## What would break this?
If someone ever removes the deny-settings mode (sets it to none) without realizing why it was there, anyone with normal contributor access could delete a piece of this stack by accident and nothing would stop them — the protection only exists because I explicitly turned it on.
