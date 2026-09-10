# Design — Invoice Submission & Approval

## The problem

Small suppliers deliver goods, invoice, then wait 60–90 days to get paid while owing
their own suppliers within 30 — borrowing at 18–24% to cover the gap. Large buyers
won't shorten cycles, and there's no mechanism to standardise or enforce agreed
payment terms. **The one date a buyer cannot manipulate is the date the invoice was
submitted — everything in this design protects that date.**

## The slice, and its explicit boundaries

Supplier submits an invoice against a PO → system validates/matches it → buyer
approves or disputes → on approval, terms and due date lock. **Out of scope, named
not built:** financing, credit assessment, movement of money, disbursement,
collections, compliance, buyer onboarding.

**What this does not solve:** it does not make money arrive sooner, and a locked due
date does not compel payment. What it produces is a trustworthy, timestamped,
mutually-agreed (or SLA-deemed) approval record — evidence a payment is late, and the
verified record invoice financing depends on, since a lender's risk premium exists
largely because it can't confirm an invoice is real, approved, and undisputed.

## Bounded contexts

| Context | Owns | Status |
|---|---|---|
| **Procurement** | PO issuance, lines, invoiceable capacity (reserved/consumed) | minimal, built |
| **Invoicing** | the supplier's claim, buyer's acceptance of it as a debt | this slice, built |
| **Payment Terms** | term length read at submission | reference data only — see below |
| **Financing** *(future)* | approved invoice as a fundable asset, not a workflow doc | boundary only |
| **Counterparty/Identity** | buyer/supplier identity | referenced by ID only |

**Language signal:** *Amount* means three incompatible things across these —
Procurement's authorisation headroom ("may I spend this?"), Invoicing's fixed
liability ("do I owe this?"), Financing's advance value ("what's it worth today?").
That divergence, not a folder split, is what makes these real boundaries. No separate
*Matching* context — matching is a policy inside Invoicing, not an owned concept.

**Payment Terms — deliberate deferral, not an omission.** For this slice, terms are
reference data (a day-count) captured **on the invoice at submission**, never read
live at approval — otherwise a buyer renegotiating terms could retroactively move an
already-submitted invoice's due date. It becomes its own bounded context once
enforcement arrives: who may change terms, what happens to in-flight invoices when
they change, whether a maximum term is enforced.

## Core aggregate: `Invoice`

**Boundary:** line items, money, terms snapshot, dispute state, due date — owned.
Purchase order, buyer, supplier — referenced by ID only.
**Entry point:** `Invoice.Submit(command, poSnapshot, termsSnapshot, matchingPolicy, clock)`.

**Invariants:**
- Submitting supplier must be the PO's vendor; PO must be open, not cancelled/exhausted.
- Line totals must reconcile to the header total; at least one line.
- Each line matched against its PO line within a **configurable tolerance** (default:
  lesser of 1% of PO line value or a small fixed amount — a placeholder, not a
  researched figure, and it would vary per buyer relationship in production). Within
  tolerance → `Submitted`. Outside tolerance → still created, as `Disputed` — a
  mismatch is a disagreement to resolve, not a malformed request.
- Submission **reserves** its amount against the PO (see Procurement, below); a PO
  distinguishes total / reserved / consumed, not just one remaining figure — otherwise
  overlapping in-flight invoices could race past the ceiling.
- **Due date = submission timestamp + term days, computed and stored at submission** —
  never typed in, never re-derived from approval. This is the central rule.
- Once `Approved`, terms, due date and lines are immutable. A correction is a **new**
  invoice referencing the disputed one — never a mutation of an approved or disputed
  record.
- A disputed invoice cannot become `Approved` without the dispute being resolved
  first (resolution = approve as-is, or reject + new corrective invoice).
- **Deemed approval:** if the buyer takes no action within a configurable review
  window, the invoice is approved and the due date stands — recorded with an explicit
  `Human`/`DeemedBySla` flag, never silently indistinguishable from a real approval. A
  real system would need this window contractually agreed, not unilaterally imposed.

## State lifecycle

```
Submit ──► Submitted ──Approve──────────► Approved (terminal, immutable)
            │  ▲          ▲
            │  │          │ (unchanged, resolved in supplier's favour)
            │  │       Disputed ──Reject──► Rejected (→ new corrective invoice)
            │  └──Dispute─┘
            └──Withdraw──► Withdrawn (releases PO reservation)

(Submitted, no action within SLA window) ──► Approved [DeemedBySla]
```
No `Draft` (no invariants, forces "unless draft" everywhere) and no `Matched` state
(matching is synchronous, recorded as data on the invoice, not a state of its own).

## Purchase order capacity (Procurement, minimal)

`Total`, `Reserved`, `Consumed`. Submit reserves; `Approve` converts reservation to
consumed; `Reject`/`Withdraw` releases it. **Known gap, not solved here:** an
abandoned `Submitted` invoice ties up capacity indefinitely — needs an expiry path.

## Async flows — kept deliberately small

Everything money- or state-critical (state change, terms snapshot, due-date
derivation, capacity reservation/consumption) is **synchronous**, inside the same
transaction as the request. Two things are genuinely async, because they're side
effects, not consistency requirements:
1. **Supplier notification** on approval/dispute — failure doesn't affect correctness;
   the invoice is the source of truth, the notification a convenience, retried
   independently.
2. **`InvoiceApproved` integration event**, for a Financing consumer that doesn't
   exist yet — written to an outbox in the same transaction as approval, so a broker
   outage can never silently lose the fact that an invoice was approved.

Not treated as async: PO capacity consumption (a real invariant, not a side effect —
made synchronous specifically to avoid a race between two concurrent submissions).

## Judgment calls made explicit (see README.md for full reasoning)

Payment Terms as reference data, not a context; due date anchored to submission with a
deemed-approval SLA; in-flight invoices reserve capacity; tolerance disputes rather
than rejects. All four are user decisions, recorded with reasoning, not defaults I
picked silently.
