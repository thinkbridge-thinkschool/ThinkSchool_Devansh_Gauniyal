# ADR 0001: Due date is computed and frozen at submission, never at approval

## Status

Accepted (Day 22, made explicit here Day 28).

## Context

The entire premise of this system, stated in `DESIGN.md`, is that small suppliers
carry the financing gap created by slow buyer payment, and no mechanism today lets a
supplier prove *when* the payment clock actually started. Whatever moment the domain
picks as "the start of the clock" has one hard requirement: the buyer — the party with
every incentive to push it later — must not be able to move it.

Two moments are on the table for that anchor: the timestamp the supplier submits the
invoice, and the timestamp the buyer approves it. Approval is the more intuitive
choice for a due date ("we agreed to pay 45 days from when we accepted this"), and
it's also the one that removes the anchor from the buyer's own control, because
approval is exactly the action a buyer can defer indefinitely — there is no external
force compelling a buyer to click "approve" today rather than in six weeks. Whichever
moment is chosen, the entire evidentiary purpose of the system (a due date a lender or
a court can trust) stands or falls on it. This forced a decision before line items,
matching tolerance, or dispute handling could even be modeled, because immutability,
the deemed-approval mechanism, and PO capacity reservation all depend on knowing which
timestamp is load-bearing.

## Decision

Due date = **submission timestamp** + payment-term days, computed once inside
`Invoice.Submit(...)` and stored on the invoice at creation. It is never re-derived,
never recalculated at approval, and never editable by either party afterward. Approval
does not set the due date — it only makes the already-computed due date (along with
terms and lines) immutable, per the state lifecycle in `DESIGN.md`.

Because the due date no longer depends on the buyer ever acting, the design pairs this
with **deemed approval**: if the buyer takes no action inside a configurable review
window, the invoice becomes `Approved` (`ApprovalKind.DeemedBySla`) and the due date
that was already sitting on the invoice simply stands. Deemed approval is a
*consequence* of this decision, not a separate one — without it, a buyer could refuse
to ever approve and leave the invoice in permanent limbo, non-terminal but past its
own due date.

## Alternatives considered

**Due date computed at approval** (`approval timestamp + term days`). Rejected: this
hands the buyer direct control of the one number the whole system exists to protect.
A buyer under cash pressure could sit on an invoice for months and the due date would
simply follow — the exact behavior the problem statement describes suppliers already
suffering from, now blessed by the system's own data model instead of fixed by it.

**Due date fixed at PO issuance** (a single date baked into the purchase order,
independent of any specific invoice). Rejected: it can't survive the reality that
invoices against the same PO are submitted at different times as deliveries happen —
one fixed date would be wrong for every invoice but the first, and it collapses
Invoicing's ownership of terms into Procurement, which `DESIGN.md`'s bounded-context
split explicitly rejects (`Amount` and now `due date` would mean different things to
different owners of the same field).

**Due date negotiable up to approval** (buyer can propose a different due date as part
of approving or disputing). Rejected: this is the approval-anchored option wearing a
disguise — the buyer still ends up moving the number after the fact, just framed as a
negotiation instead of a delay. It also breaks the immutability invariant the domain
otherwise enforces uniformly (a locked invoice can only be superseded by a new
corrective invoice, never edited in place).

## Consequences

**Accepted trade-off:** the clock starts before the buyer has necessarily verified
anything. A submission that turns out to be wrong (a genuine matching-tolerance
mismatch, or worse) still has a due date ticking from the moment it landed, and the
burden shifts to the buyer to dispute promptly rather than to the supplier to prove
delay after the fact. This is deliberate — it mirrors real invoicing practice, where
the delivery note and invoice date are what start payment terms, not the buyer's
internal approval workflow — but it does mean the design offers no protection against
a supplier who submits early or dishonestly beyond the matching-tolerance check
already in `DESIGN.md`; that risk is accepted, not solved, by this decision.

This decision also creates a direct dependency on deemed approval (see above) and on
PO capacity reservation happening at submission rather than approval — both exist
specifically because the due date can no longer wait for the buyer to act.

## Revisit if

- The matching/verification step stops being a synchronous, same-transaction check
  and becomes a genuinely multi-day process (e.g. physical goods inspection) — at that
  point, starting the clock before verification finishes may become commercially
  unacceptable to buyers, not just administratively inconvenient.
- A buyer or regulator requires due date to anchor to a formally acknowledged receipt
  event rather than unilateral submission — that would need a new `Counterparty/
  Identity`-scoped "acknowledgement" step between submission and due-date computation,
  which does not exist today.
- Evidence emerges that suppliers are gaming early/false submission to manufacture
  artificially early due dates faster than the matching-tolerance check catches it.

## Why this decision over the other candidates

Deemed-approval SLA, PO reservation on submission, modular monolith, and identity-
based Azure access were all weighed. This one was chosen because it is the only
decision the other four depend on or exist *because of* — deemed approval and
submission-time reservation are direct consequences of anchoring the due date here,
not independent choices, and getting this one wrong would have silently defeated the
system's entire stated purpose rather than merely costing rework.
