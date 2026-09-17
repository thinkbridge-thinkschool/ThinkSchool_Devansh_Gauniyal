# Threat model — STRIDE-lite (Day 27)

Scope: the capstone as deployed through Day 26 (`capstone/host/Capstone.Web`,
`capstone/host/Capstone.Worker`, SQL, Service Bus, Key Vault, managed identity —
see `infra/README.md`) plus this day's changes (network isolation for the data
tier, auth/versioning/limits on the API — see `submission-day-27-task-1.md`).
One category per STRIDE letter; each threat is tied to a real actor and a real
action against the invoice domain `DESIGN.md` describes, not a generic checklist
item. "Mitigated today" means a control that exists in the code or infra as of
this commit; "open" means a real, named gap — not a hedge.

## Spoofing — pretending to be someone you're not

**Threat: a supplier or buyer impersonates the other party to forge an approval
or a submission.** `DESIGN.md`'s central rule is that the submission timestamp
is the one date a buyer cannot manipulate — that guarantee is worthless if
whoever calls the API can claim to be any buyer or supplier they like by simply
passing a different id in the request body (every use case takes a raw
`supplierId`/`buyerId` `Guid` today — see `SubmitInvoiceCommand`).

- **Mitigated today:** endpoints now require a valid Entra ID access token
  (JWT bearer, `AddAuthentication().AddJwtBearer(...)` in `Program.cs`) — an
  anonymous caller cannot reach `/v1/invoices` at all. SQL and Service Bus
  access are both identity-based (managed identity, no shared key/connection
  string a stolen secret could reuse — Day 25).
- **Open:** the token proves *a* caller authenticated, not *which* buyer or
  supplier they are. There is no mapping yet from the authenticated principal
  to a specific counterparty id, so today's hardening stops an anonymous
  stranger, not a legitimate supplier calling the API as if they were a
  different supplier. That mapping (claims → counterparty identity, checked
  inside `SubmitInvoiceUseCase`/`ApproveInvoiceUseCase`) is real, unbuilt work
  — the `Counterparty/Identity` context `DESIGN.md` names as "referenced by ID
  only" is exactly the missing piece.

## Tampering — altering data or requests in flight or at rest

**Threat: a buyer alters a due date, or a line amount, after the fact to delay
payment or shrink what they owe.** This is the exact attack `DESIGN.md`
protects against by design: due date is computed once at submission and
frozen (`Invoice.Submit`), and once `Approved`, terms/due date/lines are
immutable in the domain model.

- **Mitigated today:** the invariant lives in the `Invoice` aggregate itself
  (25 domain tests assert it — `capstone/tests/Capstone.Invoicing.Domain.Tests`)
  — there is no code path that mutates a locked field after approval. HTTPS
  everywhere (`httpsOnly: true` on both Web Apps) stops on-the-wire tampering
  with the request. TLS 1.2 minimum on SQL and Service Bus.
- **Open:** the domain invariant only holds because persistence is in-memory
  and single-process today (`InMemoryInvoiceRepository`). There is no database
  write path yet to attack, but there is also no optimistic-concurrency token,
  audit trigger, or row-level protection defined for when EF Core lands
  (Day 28+) — a direct `UPDATE` against the eventual `Invoices` table,
  run by anyone with `db_datawriter` (today: only the API's managed identity
  and the Entra admin — see `infra/README.md`), would bypass the aggregate
  entirely. No request-body integrity check (e.g. a signature) exists — the
  new input-size/shape limits (below) reduce the attack surface but don't
  authenticate the payload's content, only its author.

## Repudiation — denying having done something

**Threat: a buyer approves an invoice, then later denies having approved it
(or claims they approved a different version) once the due date passes and
payment is late.** This is the entire point of the domain, per `DESIGN.md`:
"what it produces is a trustworthy, timestamped, mutually-agreed... approval
record — evidence a payment is late."

- **Mitigated today:** every state transition carries an `ApprovalRecord`
  with an explicit `Human`/`DeemedBySla` `ApprovalKind` (`ApprovalKind.cs`,
  `Events.cs`) — a real approval and an SLA-deemed one are never conflated.
  OpenTelemetry traces every request end-to-end with a trace id
  (Day 26) — a specific approval call is reconstructable from Application
  Insights `requests`/`traces` for as long as the 30-day retention holds.
- **Open:** there is no persisted, tamper-evident audit log independent of the
  application's own database — today, "the record" and "the thing being
  disputed" are the same mutable store (in-memory now, one SQL table later).
  A party with direct database access could alter history without leaving a
  distinguishable trace from a legitimate write. No non-repudiation signature
  ties a specific authenticated principal (once Spoofing's gap above is
  closed) to a specific `ApprovalRecord` row.

## Information disclosure — reading data you shouldn't

**Threat: one buyer or supplier reads another party's invoices** — line
amounts, payment terms, dispute status are exactly the kind of commercially
sensitive data a competitor or counterparty has no right to see.

- **Mitigated today (this day):** SQL is no longer reachable from the public
  internet at all — `publicNetworkAccess: 'Disabled'`, reachable only via a
  private endpoint from inside `vnet-capstone-{env}` (see "Private endpoints"
  in the submission notes). Even a leaked SQL admin credential is now useless
  from outside Azure's private network path. Application Insights ingestion
  requires the managed identity's token (`DisableLocalAuth: true`, Day 25) —
  a leaked connection string alone cannot exfiltrate telemetry. No plaintext
  secret exists in any app setting (verified again this day, see submission
  notes).
- **Open, and real: Service Bus has no network restriction at all.**
  Confirmed live this session, twice, against this project's actual Standard
  namespace: a private endpoint was rejected outright
  (`PrivateEndpointInvalidSku`), and a `networkRuleSets` restriction (IP/VNet
  rules — no private IP, but still a real access filter) was *also* rejected
  (`InvalidSkuForNetworkRuleSet`). IP firewall rules, VNet rules, and private
  endpoints are all Premium-only on Service Bus — there is no partial or
  cheaper network control available on Standard, only the identity-based
  auth already in place since Day 25. Upgrading to Premium (confirmed live at
  $0.9275/hour for the smallest SKU, ~69x Standard's cost) was ruled out as
  disproportionate for a namespace nothing in `capstone/src` publishes to or
  consumes from yet (see README.md's Day 23 section, same reasoning already
  applied there). This means: today, anyone on the public internet who
  obtains a valid Entra ID token for this namespace's data-plane roles (the
  API's own managed identity token, specifically) could reach it from
  anywhere — the only thing standing between "has a token" and "can read/send
  on this namespace" is that the token itself is hard to obtain, not a
  network boundary. A real production deployment handling actual invoice
  data over Service Bus should treat this as a required Premium upgrade, not
  a deferred nice-to-have.
- **Open:** the API itself still returns whatever `IInvoiceRepository`
  gives it with no per-caller filtering — `GET /v1/invoices` returns every
  invoice in the store to any authenticated caller, not just the caller's
  own. This is the direct consequence of the Spoofing gap above: without a
  claims → counterparty mapping, there is no basis on which to filter. This
  is named here plainly rather than silently left out: the new endpoint
  exists specifically to have something real to harden (see submission
  notes), and it is **not** access-controlled per party today. TLS
  terminates at the App Service platform edge, not end-to-end to a private
  origin the customer controls — acceptable for a scaffold, not for a real
  multi-tenant deployment.

## Denial of service — making the system unavailable to legitimate users

**Threat: a competitor or a bad actor floods the invoice-submission endpoint,
either to hide a fraudulent late submission in a burst of noise, or simply to
deny a supplier the ability to submit before a contractual deadline.**

- **Mitigated today:** a fixed-window rate limiter (`AddRateLimiter`,
  100 requests/minute per client, 429 on rejection) sits in front of every
  `/v1` endpoint. Request body size is capped (16 KB) so a single oversized
  payload can't exhaust memory or bandwidth disproportionately. String
  fields (`InvoiceNumber`, line descriptions) and page sizes (`pageSize`,
  clamped to 100) are bounded so a single request can't force an unbounded
  scan or response. App Service plan has `alwaysOn` and a defined SKU
  (no burst-to-infinite autoscale that would turn a flood into an
  uncapped bill).
- **Open:** the rate limiter is per-*App Service instance*, in-memory — with
  more than one instance (prod is 2, per `parameters/prod.bicepparam`) a
  determined attacker gets roughly `instances × limit`, not one global limit.
  There is no WAF or Azure Front Door in front of the app, so nothing filters
  traffic before it reaches the App Service plan's own compute and network
  quota. SQL Serverless auto-pause (dev only) means a flood that never quite
  triggers the rate limit can still force expensive cold-starts against a
  paused database, a real (if minor, dev-only) cost/availability lever.

## Elevation of privilege — doing something you're not authorized to do

**Threat: an authenticated but ordinary caller performs an action reserved
for a buyer's authorized approver — approving their own invoice as if they
were the buyer, or an ordinary supplier user granting themselves the SQL
admin's rights.**

- **Mitigated today:** every managed identity is scoped to the minimum role
  it needs and nothing more (Day 25's table in `infra/README.md`: Key Vault
  Secrets User, Service Bus Data Sender/Receiver only, `db_datareader`/
  `db_datawriter` — never `db_owner` or a server-level role). The SQL Entra
  admin is a single named human principal, not the application identity —
  the app cannot grant itself more access even if compromised. `.RequireAuthorization()`
  now gates every `/v1` route; there is no anonymous path to a
  state-changing operation.
- **Open:** as in Spoofing, there is currently exactly one authorization
  tier — "has a valid token" — with no role/claim distinguishing "buyer
  approver" from "supplier submitter" from "read-only viewer." Any
  authenticated caller can call `ApproveInvoiceUseCase` for any invoice.
  This is the same underlying gap (no real counterparty/role claim mapping)
  showing up a third time, under a third STRIDE letter — worth naming once,
  explicitly: **closing the Counterparty/Identity mapping gap would resolve
  the open item in Spoofing, Information disclosure, and Elevation of
  privilege simultaneously**, because all three currently reduce to "the API
  knows a token is valid but not whose it is, in domain terms."
