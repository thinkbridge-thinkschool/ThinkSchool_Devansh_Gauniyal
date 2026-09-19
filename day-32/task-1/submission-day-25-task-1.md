# Day 25 Task 1 — Identity end-to-end

## Notes for mentor

This continues from `capstone/submission-day-24-task-1.md`. Same reason as
every prior capstone day: lives at `capstone/infra/`, not a `day-25/task-1`
folder — a frozen, byte-identical snapshot of `capstone/` as it stands today
is committed separately at `day-25/task-1/` for browsing, but all development
happened in `capstone/`. Commit hash representing this day's state: this same
commit — see `git log -1` or the GitHub commit view (a commit can't contain
its own hash inside its own tree).

**Unlike Day 24, the dev stack is intentionally left running after this
commit** — Week 5 builds on this same infrastructure. Nothing was torn down.
See the closing section below for what's live and the exact command to tear
it down later.

### Managed identity wiring (`modules/identity.bicep` + `modules/api.bicep`)

One user-assigned identity, referenced by the Web App (not system-assigned —
see `capstone/infra/README.md`, "Why a user-assigned identity" for why: it
has to exist and be grantable before the Web App does, and it survives Web
App recreation, unlike a system-assigned identity's grants):

```bicep
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-capstone-${environmentName}-api'
  location: location
  tags: tags
}
```

```bicep
resource webApp 'Microsoft.Web/sites@2026-07-15' = {
  ...
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityResourceId}': {}
    }
  }
  properties: {
    ...
    keyVaultReferenceIdentity: apiIdentityResourceId
    ...
  }
}
```

### A Key Vault reference (`modules/api.bicep`'s app settings)

```bicep
{
  name: 'DemoConfig__SampleSetting'
  value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${keyVaultDemoSecretName})'
}
```

Read back live from the deployed Web App, this resolves to:
`@Microsoft.KeyVault(SecretUri=https://kv-capstone-dev-klzi66.vault.azure.net/secrets/DemoConfig--SampleSetting)`
— proven to actually resolve (not just accepted as valid syntax) via
`az rest` against the Web App's own `configreferences/appsettings` endpoint,
which returned `"status": "Resolved"`.

### App settings — zero plaintext secrets, read back live

`az webapp config appsettings list` against `capstone-dev-api-f7nsoj`, all
four settings present:

| App setting | Value shape |
|---|---|
| `AZURE_CLIENT_ID` | `1b8d1172-ac6a-4c69-83c1-5729904bc38c` — an identifier, not a credential |
| `ConnectionStrings__CapstoneDb` | `Server=sql-capstone-dev-2fqdji.database.windows.net;Authentication=Active Directory Managed Identity;Encrypt=True;User Id=1b8d1172-ac6a-4c69-83c1-5729904bc38c;Database=sqldb-capstone-dev` — no password field |
| `ServiceBus__FullyQualifiedNamespace` | `sb-capstone-dev-tnjxju.servicebus.windows.net` — a hostname, and `disableLocalAuth: true` means there's no SAS key it could pair with anyway |
| `DemoConfig__SampleSetting` | the Key Vault reference above — a pointer, not a value |

No connection string, key, or password appears in plain text anywhere in
this configuration. Full reasoning for why each shape is safe in
`capstone/infra/README.md`, "What zero secrets actually looks like here."

### SQL and Service Bus identity-access evidence, including the unauthorized case

Full commands and raw output: `capstone/infra/output/identity-connectivity-proof.txt`.

- Real Bearer tokens minted from inside the Web App's own container (via the
  platform identity endpoint — the only place a managed identity's token can
  be obtained) scoped to both `https://database.windows.net/` and
  `https://servicebus.azure.net/`, using this identity's client id.
- A real message send to the `invoice-approved-events` Service Bus topic
  using that token succeeded: `HTTP 201`.
- **Unauthorized case**: the same token, used to attempt creating a queue (an
  action needing the Manage claim, which "Azure Service Bus Data Sender" does
  not grant), was rejected live: `HTTP 401`,
  `Authorization failed for specified action: Manage,EntityWrite`. A send
  attempt with no Authorization header at all was also rejected: `HTTP 401`,
  `MissingToken` — confirming `disableLocalAuth: true` closed the old SAS-key
  path, not just that nothing currently uses it.
- SQL: the manual database grant (below) was independently verified via
  `sys.database_role_members`, and the identity's token acquisition for the
  `database.windows.net` audience succeeded — see the output file's own
  "Honest limits" note on why a live TDS query wasn't additionally run this
  session.

### Manual steps — performed by Devansh, not automated

Two things could not be done by me and were done by hand:

1. **Running the database-level grant** — `CREATE USER [id-capstone-dev-api]
   FROM EXTERNAL PROVIDER;` and the two `ALTER ROLE ... ADD MEMBER` statements,
   in the Azure Portal's Query Editor, connected as the Entra administrator.
   There is no ARM/Bicep resource type for a database-level T-SQL grant — only
   a connection authenticated as the Entra admin can run it. Devansh also
   allowlisted his own client IP through the portal's Query Editor when the
   server's firewall first blocked the connection.
2. **Verifying the grant** — Devansh ran the `sys.database_role_members`
   query himself and confirmed exactly two rows
   (`db_datareader` and `db_datawriter`, both for `id-capstone-dev-api`).

Everything else — the identity, both role assignments, the Entra admin
designation, Key Vault and its secret, and every app setting — was declared
in Bicep and deployed automatically by `az stack sub create`.

### What's left running, and how to tear it down later

`rg-capstone-dev` (16 resources: API host, SQL server + database, Service Bus
namespace + 2 topics, the managed identity, Key Vault + 1 secret, the Entra
admin designation, and 3 role assignments) is **intentionally still live** —
Week 5 continues on this same infrastructure. Estimated cost while running:
roughly $0.75-0.80/day (App Service B1 ≈ $0.43/day, Service Bus Standard's
flat base charge ≈ $0.33/day, SQL serverless dev covered by the free monthly
limit ≈ $0/day, Key Vault operations negligible), verified live against the
Azure Retail Prices API for Central India on 2026-09-09, not guessed.

Teardown, when Week 5 is done with it:

```bash
az stack sub delete --name capstone-dev --action-on-unmanage deleteAll --yes
```

## What did you learn this session?
A managed identity's token can only ever be minted from inside the Azure resource it's attached to — my own `az` CLI session, no matter how well-authenticated, can never get one, which is why every proof here had to run from inside the Web App itself.
I also learned App Service only injects the identity endpoint variables into the actual site worker process, not into the container generally — Kudu's own command shell and a fresh SSH session both see a completely different, narrower environment.

## What would break this?
If `id-capstone-dev-api` were ever deleted, the Web App would lose its only way to reach SQL, Service Bus, and Key Vault all at once, with no second identity or fallback credential to fall back on.
