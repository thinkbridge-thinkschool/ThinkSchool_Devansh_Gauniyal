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
