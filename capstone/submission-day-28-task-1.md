# Day 28 Task 1 — Design review + ADR

## Notes for mentor

### 1. The ADR

Full record: [`adr/0001-due-date-anchored-to-submission.md`](adr/0001-due-date-anchored-to-submission.md).

**Decision:** due date is computed from the submission timestamp and frozen at
`Invoice.Submit(...)` — never recalculated at approval, never editable afterward.
Chosen over the deemed-approval SLA, submission-time PO reservation, modular monolith,
and identity-based Azure access because it's the only one of the five the other four
are *consequences of* — deemed approval and submission-time reservation both exist
specifically because this decision means the due date can no longer wait on the buyer
to act. Getting this one wrong would have quietly defeated the system's entire stated
purpose (a due date a buyer can't manipulate); the other four would have cost rework,
not the point of the project.

### 2. The critique

Three weakest points, argued straight:

1. **Deemed approval can race a real dispute.** `DESIGN.md` said the SLA sweep
   converts a `Submitted` invoice to `Approved` if the buyer does nothing — but never
   said what stops the sweep from firing on a stale read at the exact moment a buyer's
   dispute is landing, converting a disputed invoice into `Approved` by pure timing
   luck. That's not a hypothetical edge case; it's the exact kind of silent due-date
   manipulation the whole design exists to prevent, just relocated from the buyer to a
   race condition.
2. **PO capacity reservation has no expiry for disputes either.** `DESIGN.md` names
   "an abandoned `Submitted` invoice ties up capacity indefinitely" as a known gap, but
   a `Disputed` invoice holds the same reservation with no expiry named at all — and a
   live disagreement plausibly runs longer than mere silence, so the gap the design
   admits to is smaller than the one that actually exists.
3. **A mismatched submission reserves capacity before anyone judges whether the
   mismatch is honest.** Outside-tolerance invoices become `Disputed` rather than
   rejected, which is the right call for genuine variance — but it means a wildly
   wrong header total still reserves against the PO the instant it's submitted, with
   no check that the reserved amount is proportionate to the line items actually in
   question.

**Acted on #1** — it's the one that could undo ADR 0001 itself, not just a resource
bookkeeping gap. Amended `DESIGN.md`'s invariants with an explicit ordering rule: the
SLA sweep may only transition an invoice still `Submitted` at the instant it acts,
checked atomically against persisted state, and a dispute recorded first always wins.
\#2 and #3 are left as named, unfixed gaps — real, but smaller in consequence than a
race that could produce an untrustworthy `Approved` record, which is the one outcome
ADR 0001 is supposed to make impossible.

**Mentor feedback:** not available yet for this exercise. The critique above is
self-generated, pending a real review — space reserved here for it once given.

### 3. Day-by-day build plan, Days 29–32

**Honest starting point:** the capstone today is a real domain model (`Invoice`,
`PurchaseOrder`, 25 passing domain tests, 5 architecture tests) and real infrastructure
(SQL, Service Bus, Key Vault, managed identity, JWT auth, rate limiting — all
provisioned and hardened through Day 27), but almost no application surface connecting
them: two demo endpoints, in-memory repositories, no persistence, no background
processing. **Day 29 carries the most weight of the four** — it's where the scaffold
becomes a running application for the first time, not just a passing test suite.

**Reusable starting points already in this repo**, rather than writing cold:
- `day-20/task-1/src/OutboxDemo` (`OutboxRelayBackgroundService`, `IdempotentConsumer`,
  the outbox EF migration) is close to exactly what `DESIGN.md`'s `InvoiceApproved`
  outbox needs — same pattern (write to an outbox table in the approval transaction,
  relay it out-of-band), different payload.
- `day-2/task-7/QuotesApi` (`QuoteRepository`, `QuotesDbContextFactory`, EF migrations,
  JWT auth already wired in `Program.cs`) is the closest existing example of the
  EF Core repository + DbContext-factory + migration shape the in-memory repositories
  need to be replaced with — the JWT piece is already superseded by capstone's own
  Day 25/27 auth, but the persistence shape is directly transferable.

**Day 29 — Foundation and happy path.**
Build: EF Core `DbContext`s for Invoicing and Procurement (replacing the
`ConcurrentDictionary` repositories) against the existing private-endpoint SQL server;
real endpoints (`POST /v1/purchase-orders`, `POST /v1/invoices`,
`GET /v1/invoices/{id}`, `POST /v1/invoices/{id}/approve`) wired to the existing use
cases instead of `/demo/*`; migrations checked in.
Done: a real HTTP call submits and approves an invoice against real Azure SQL, due
date computed and locked exactly as `Invoice.Submit` already guarantees, all 25
existing domain tests plus one new integration test for this path pass.
Cut first if short: dispute and withdraw endpoints, and the deemed-approval sweep —
push whole to Day 30. Not cut: real persistence and the happy path, since everything
later depends on both existing.

**Day 30 — Feature completeness.**
Build: dispute and withdraw endpoints and their PO-capacity release paths; the
deemed-approval SLA sweep as a hosted background service, built with the ordering
invariant from the critique above (conditional update against persisted state, dispute
wins ties) designed in from the start rather than retrofitted; the
`Counterparty/Identity` claims-to-party mapping named as the single gap behind three
different `THREAT-MODEL.md` findings (Spoofing, Information disclosure, Elevation of
privilege), plus per-caller filtering on `GET /v1/invoices` that depends on it.
Done: every state in `DESIGN.md`'s lifecycle diagram is reachable over HTTP, and an
authenticated caller can only see and act on their own party's invoices.
Cut first if short: the sweep can degrade to a manually-triggered endpoint instead of
a real timer. Not cut: the identity mapping and per-caller filtering — leaving those
out ships a known, already-documented security hole.

**Day 31 — Polish: tests, performance, security.**
Build: integration tests against real SQL (not in-memory) for every lifecycle
transition, including a concurrency test that actually exercises the Day 30 sweep/
dispute race; indexes on the new `Invoices`/`PurchaseOrders` tables checked against
query plans; the `InvoiceApproved` outbox wired using the Day 20 pattern named above; a
re-run of the Day 27 ZAP baseline against the now-real API surface.
Done: the concurrency test passes deterministically (not "usually"), the outbox row is
written in the same transaction as approval, ZAP shows no new regressions versus
Day 27's clean baseline.
Cut first if short: the outbox *relay* to Service Bus can stay unwired (row written,
not yet shipped) — already consistent with Service Bus's documented open network gap
in `THREAT-MODEL.md`. Not cut: the concurrency test, since it's the only proof the
Day 28 critique fix actually holds under load rather than just reading correctly.

**Day 32 — Ship, demo, postmortem.**
Build: deploy through the existing `azd` environment (no new infrastructure); a demo
script covering submit → approve, submit → dispute → reject → corrective invoice, and
one deemed-approval; a postmortem documenting what shipped, what was cut, and which
`THREAT-MODEL.md`/`DESIGN.md` gaps still stand (Service Bus network isolation,
per-relationship matching tolerance, PO reservation expiry — now including disputes,
per this day's critique).
Done: the live demo runs end to end against the deployed environment.
Cut first if short: nothing — if Day 31 slipped, ship what exists and say so plainly
in the postmortem rather than rushing extra scope into the last day.

**What must not slip later than scheduled:** EF Core persistence (Day 29) has to land
before dispute/withdraw (Day 30) act on real stored invoices, and the sweep's
conditional-update concurrency handling has to be designed alongside persistence on
Day 29 even though the dispute feature it protects against ships Day 30 — retrofitting
optimistic concurrency after the fact risks a rewrite of the approve/dispute paths
rather than an addition to them.

### Live project and snapshot

The live project is `capstone/` — `day-28/task-1/` is a frozen snapshot of it as this
day ended, never edited directly.

Commit representing this day's state: `<commit-hash-filled-in-after-commit>`.

The critique above is self-generated pending real mentor feedback — the section is
marked for it explicitly rather than left implicit.

## What did you learn this session?
Writing the ADR made me realize the deemed-approval SLA and the PO reservation-at-submission rule aren't separate decisions I made — they're both just consequences of anchoring the due date to submission. Once I saw that, picking which decision "mattered most" stopped being a toss-up.
Also, self-critiquing a design you just wrote is a different skill from writing it — you have to argue against your own reasoning instead of just checking it still holds.

## What would break this?
A race between the SLA sweep and a buyer's dispute landing at nearly the same moment — without an explicit ordering rule, timing alone could decide whether a disputed invoice quietly becomes "approved."
