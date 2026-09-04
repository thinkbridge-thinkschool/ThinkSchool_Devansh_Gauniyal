# Day 22 Task 2 — Capstone kickoff: design + scaffold

## Notes for mentor

One-page design in [`DESIGN.md`](DESIGN.md): bounded contexts (Procurement,
Invoicing, Payment Terms deferred as reference data, Financing named as a boundary
only), the `Invoice` aggregate and its invariants, the state lifecycle (including
deemed approval), and the two genuinely async flows (supplier notification, an
`InvoiceApproved` outbox event) — everything else is kept synchronous on purpose.

Scaffolded solution: a modular monolith under `capstone/` — `SharedKernel`, and
`Procurement`/`Invoicing` modules each split into `Domain`/`Application`/
`Infrastructure` projects, a thin `Capstone.Web` composition root, plus two test
projects (25 domain tests, 5 architecture tests enforcing the dependency rule). Full
layout and build/run instructions in [`README.md`](README.md).

Capstone lives at `capstone/`, not `day-22/task-2`, because it's one continuous
project spanning this kickoff and Days 28–32 — see README.md, "Why this lives at
`capstone/`."

This day's state is commit `72c77b8` on branch `day-22/task-2`.

## What did you learn this session?

Anchoring the due date to submission instead of approval is the whole point — if it moved with approval, a buyer could stall forever and nothing would be fixed. Getting the module boundary to actually compile-fail on a wrong direction, not just look tidy in folders, took real project-reference discipline.

## What would break this?

If Invoicing ever needed a live read from Procurement mid-approval instead of a snapshot from submission, the "submission time is fixed" guarantee would get shakier. And the abandoned-invoice capacity gap is real — nothing here expires a stuck reservation yet.
