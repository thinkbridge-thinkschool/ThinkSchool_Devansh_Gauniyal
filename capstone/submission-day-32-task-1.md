# Day 32 Task 1 — Ship + demo + postmortem

**Live URL:** https://capstone-prod-api-kjritd.azurewebsites.net — expires with this
subscription's Azure credit on **2026-09-28**. After that date this link stops
resolving; that's the credit running out, not evidence the project was ever broken.

**Live project and snapshot:** the live project is `capstone/` — `day-32/task-1/` is a
frozen snapshot of it as this day ended, never edited directly.

**Commit representing this day's state:** _[fill in after pushing — see the
follow-up commit that records it, same pattern as every prior day]_

## Notes for mentor

Prod's infrastructure (`rg-capstone-prod`) turned out to already exist, deployed
2026-09-15 — ahead of this day's own narrative, undocumented in any prior submission.
Rather than quietly building on it or tearing it down to match the "prod is deployed
today" framing, I stopped, showed the discovery and its cost, and got explicit
direction to reuse it. What "shipping live" meant today, concretely: deploy the
current Day 31 app code onto that existing infrastructure, apply its three pending EF
migrations (the database was empty — the infra predates persistence code), and bring
its app settings back in line with what `infra/modules/api.bicep` now declares (the
two Day 30 async-flow topic names had only ever been applied out-of-band to dev, never
to prod — `OutboxRelayBackgroundService` was throwing `ServiceBus:
InvoiceApprovedEventsTopicName is not configured` on every tick until this was fixed
today).

**Migration path, since prod's SQL has been private-endpoint-only since it was
provisioned:** applied by hand, by the mentor, connected as the Entra admin via the
Portal's Query Editor (temporary public network access), granting
`db_ddladmin`/`db_datareader`/`db_datawriter` to `id-capstone-prod-api` — then the
app's own `MigrateAsync()` call applied all three migrations on startup using its own
managed identity, exactly the same mechanism Day 29 proved for dev. `db_ddladmin` was
revoked and public network access re-disabled immediately after, confirmed live.

**Real evidence, captured today, not reconstructed:**
- `POST /demo/submit-sample-invoice` against the live prod URL →
  `{"purchaseOrderId":"d4597c40-595f-4461-8906-2d9771c1954d","invoiceId":"55ab87e3-ee7e-4a47-ae46-5b8b62566991"}`,
  HTTP 200 — the full Procurement → Invoicing round trip, against real Azure SQL,
  through the schema this day's migrations created.
- All nine `/v1/*` routes (purchase-orders, invoices, approve, dispute, reject,
  withdraw, deemed-approval-sweep, get-by-id, list) return `401` with no token — every
  route from Days 27–31 is live and still auth-gated, none silently dropped.
- `az sql server show` on `sql-capstone-prod-bjpfnk` → `publicNetworkAccess: Disabled`
  — reconfirmed after the migration window closed.
- A fresh ZAP baseline against the live prod URL today: 66 PASS, one
  informational-only finding ("Non-Storable Content" — by design, matches Day 27/31's
  dev result exactly). Report: `security/zap/zap-baseline-prod-day32.json`/`.html`.
- `dotnet test Capstone.slnx`: 55/55 passing today (29 domain, 5 architecture, 12
  integration against real SQL Server via Testcontainers, 9 API), `dotnet build
  -warnaserror`: 0 warnings.
- Capstone CI, `main` and `day-31/task-1`: both green as of this session (checked via
  the public, unauthenticated GitHub Actions API — no `gh` needed —
  `https://api.github.com/repos/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs`).
  This branch's own CI run: see "Manual steps left" below.

Full detail on what was verified, what regressed, and what stayed intact is in the
report given alongside this submission.

## The demo script

**~5 minutes. Lead with the problem, not the architecture.**

**0:00–0:45 — the problem.** Say: "Small suppliers deliver goods, invoice a buyer,
then wait 60 to 90 days to get paid — while they still owe their own suppliers within
30. So they borrow at 18 to 24 percent just to bridge that gap. The reason this
persists is that there's no way for a supplier to *prove* when the payment clock
actually started, because the one date that matters — the submission date — is also
the one date a buyer has every incentive to push later. This project builds the part
of that problem that software can actually fix: an invoice workflow where the due
date is locked the moment it's submitted, not whenever the buyer gets around to
approving it."

**0:45–1:30 — it's live, not a slide.** Show the terminal:
```
curl https://capstone-prod-api-kjritd.azurewebsites.net/
```
Expected: `{"message":"Capstone host is running."}`, HTTP 200. Say: "This is running
in Azure right now — App Service, Azure SQL behind a private endpoint, Service Bus,
all provisioned as code. Not a localhost demo."

**1:30–2:15 — the round trip, against the real database.**
```
curl -X POST https://capstone-prod-api-kjritd.azurewebsites.net/demo/submit-sample-invoice
```
Expected: a JSON body with a real `purchaseOrderId` and `invoiceId` (GUIDs), HTTP 200.
Say: "That just created a purchase order and submitted an invoice against it, matched
within tolerance, through a real Azure SQL database reachable only over a private
endpoint — no public network path to that data at all."

**2:15–3:00 — auth is real, not decorative.**
```
curl -i https://capstone-prod-api-kjritd.azurewebsites.net/v1/invoices
```
Expected: HTTP 401, no body. Say: "Every real endpoint requires a valid Entra ID
token. There's no anonymous path to see or change an actual invoice." *(Have a real
bearer token ready before recording — mint one against the `capstone-api-prod` app
registration; this can't be scripted into this file since minting a client secret is a
deliberate one-time action, not something to automate.)* With the token, repeat the
call:
```
curl -H "Authorization: Bearer $TOKEN" https://capstone-prod-api-kjritd.azurewebsites.net/v1/invoices
```
Expected: HTTP 200, a paginated JSON list.

**3:00–4:15 — the due date can't be manipulated. This is the actual point.** Two
ways to show it, pick whichever fits the time:
- *Fastest:* open `adr/0001-due-date-anchored-to-submission.md` on screen and read the
  Decision section aloud — due date is computed once, at submission, never
  re-derived at approval.
- *More convincing:* open
  `tests/Capstone.Integration.Tests/InvoiceLifecycleTests.cs`, find
  `SubmitThenApprove_PersistsToRealDatabase_WithDueDateLockedAtSubmission`, and walk
  through it live: the test submits an invoice, advances the clock by a day, approves
  it, reloads from a fresh database connection, and asserts the due date still equals
  `submittedAt + 45 days` — not `approvedAt + 45 days`. Run it on screen:
  `dotnet test --filter SubmitThenApprove_PersistsToRealDatabase_WithDueDateLockedAtSubmission`
  and show it pass. Say: "However long the buyer sits on approval, the due date
  doesn't move. That's the one guarantee this whole system exists to provide."

**4:15–5:00 — the honest boundaries.** Say: "This doesn't move money, assess credit,
or decide who gets financed — those are named as explicitly out of scope in the
design. What it produces is a trustworthy, timestamped approval record: evidence a
payment is late, and the kind of record invoice financing depends on, because a
lender's risk premium today exists largely because they can't confirm an invoice is
real, approved, and undisputed." Close on the README or DESIGN.md's boundary table.

**Recording link:** _[paste the recording URL here once it's uploaded]_

## The one-page postmortem — DRAFT, mine to rewrite in my own voice

*(Grounded in the real build history across Days 22–31. Rewrite this in first person,
in your own words — this is a starting point, not a final answer.)*

**What I'd do differently.** Infrastructure was built Days 23–27, entirely ahead of
the application that would use it: SQL, Service Bus, and a managed identity were all
provisioned and granted real permissions while `capstone/src` still held nothing but
in-memory repositories. The identity got its Key Vault and Service Bus roles before a
single real query existed to make. Day 26 needed to invent a throwaway worker
(`Capstone.Worker`, whose only job was giving a distributed trace a second hop to
stitch across) specifically because there was no real async consumer yet to prove
tracing against. Was that ordering right? Partly. Provisioning SQL and Service Bus
early meant Day 29's persistence work landed against infrastructure already hardened
and tested, rather than bolting security on after the fact — a real advantage. But
granting a managed identity real roles against resources nothing calls yet, and
inventing a demo consumer just to have something to trace, both cost real time
building scaffolding whose only job was proving infrastructure worked in isolation
from the application it was for. Building the happy path first, then hardening the
infrastructure it actually exercises, would have meant every piece of infrastructure
had a real caller from day one — no throwaway worker, no identity grant sitting idle
for four days waiting for code that could use it.

**What the hardest bug taught me.** The hardest bug wasn't the loudest one. Day 26 had
two bugs that crashed immediately and loudly (a DI type-mismatch that SIGABRT'd the
worker container, a split NuGet package that threw `ArgumentException` on the first
real SQL connection) — both were fixed within the same session because the failure
was impossible to miss. The real problem was a captive-dependency bug found on Day 31:
`Invoicing.Application`'s PO-capacity port was registered as a Singleton depending on
a Scoped EF Core repository. It never threw. It had been silently live since Day 29,
across every deployment through Day 30, quietly holding onto a stale or disposed
database connection instead of failing fast — because ASP.NET Core's production
configuration doesn't validate the DI object graph by default the way local
development does. It only surfaced because running the app locally the ordinary way,
for an unrelated reason, happened to trip the validation dev mode performs. The
lesson: "it works when deployed" and "it's wired correctly" are not the same claim,
and a bug that fails silently is more dangerous than one that crashes, because nothing
forces you to go looking for it.

**What I'm proudest of.** Not the project as a whole — one specific guarantee: deemed
approval can never win a race against a real, contemporaneous dispute. The SLA sweep
that auto-approves an abandoned invoice only transitions an invoice that is still
`Submitted` at the exact instant it acts, checked and updated atomically against the
persisted state — never a stale in-memory read. Without that, a buyer's genuine,
last-second dispute could lose to the sweep purely on timing, silently turning a
disputed invoice into an `Approved` one. That single guarantee is the thing standing
between "the due date on an approved invoice is trustworthy" (ADR 0001's entire
premise) and a system that quietly defeats its own point under exactly the kind of
timing pressure a real production system would actually see.

## What did you learn this session?
Prod infra can drift from what the submission history says happened — a
Deployment Stack from four days ago, with no submission mentioning it, taught me to
verify live state before trusting the narrative.

## What would break this?
Two people redeploying prod at once, one running migrations while the other
resets app settings — nothing today locks the deploy step itself, only the data it touches.
