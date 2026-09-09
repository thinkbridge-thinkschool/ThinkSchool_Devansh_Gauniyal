# Day 25 Task 1 — Identity end-to-end

## Notes for mentor

This continues from `capstone/submission-day-24-task-1.md`: `capstone/` is
the live project, `day-25/task-1/` is today's frozen, byte-identical
snapshot, and this commit is the one to point to for this day's state (see
`git log -1`). A user-assigned managed identity is now attached to the API
(`capstone/infra/modules/identity.bicep`, `modules/api.bicep`), granted
least-privilege roles on Key Vault and Service Bus and set as the vehicle for
Entra-only SQL auth (`modules/sql.bicep`). All four of the Web App's app
settings read back live — no plaintext secret in any of them, including a
Key Vault reference confirmed to actually resolve — with the full reasoning,
commands, and raw evidence (token acquisition, an authorized send, and two
real unauthorized-case rejections) in `capstone/infra/README.md` and
`capstone/infra/output/identity-connectivity-proof.txt`.

Two steps had no ARM/Bicep equivalent and were done by hand by Devansh in the
Azure Portal's Query Editor, connected as the Entra administrator: the
database-level `CREATE USER`/`ALTER ROLE` grant, and its own verification via
`sys.database_role_members`. Unlike Day 24, the dev stack (`rg-capstone-dev`,
16 resources) is **intentionally left running** — Week 5 continues on this
infrastructure — with its estimated running cost and exact teardown command
in `capstone/infra/README.md`'s "What's running right now, and its cost"
section.

## What did you learn this session?
A managed identity's token can only ever be minted from inside the Azure resource it's attached to — my own `az` CLI session, no matter how well-authenticated, can never get one, which is why every proof here had to run from inside the Web App itself.
I also learned App Service only injects the identity endpoint variables into the actual site worker process, not into the container generally — Kudu's own command shell and a fresh SSH session both see a completely different, narrower environment.

## What would break this?
If `id-capstone-dev-api` were ever deleted, the Web App would lose its only way to reach SQL, Service Bus, and Key Vault all at once, with no second identity or fallback credential to fall back on.
