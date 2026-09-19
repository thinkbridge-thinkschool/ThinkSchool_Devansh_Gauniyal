# Capstone — Invoice Submission & Approval

Small suppliers deliver goods, invoice a buyer, then wait 60–90 days to get paid —
while owing their own suppliers within 30, so they borrow at 18–24% just to cover the
gap. Nothing today lets a supplier prove *when* that payment clock actually started,
because the one date that matters is also the one date a buyer has every incentive to
push later. This project builds the part of that problem that's actually solvable in
software: a submission → match → approve workflow where the due date is computed once,
at submission, and frozen — not something a buyer's delay can move. See
[`DESIGN.md`](DESIGN.md) for the full design (contexts, aggregate, invariants, state
lifecycle, async flows) and [`adr/0001-due-date-anchored-to-submission.md`](adr/0001-due-date-anchored-to-submission.md)
for why that one decision is the load-bearing one.

**What this slice does:** supplier submits an invoice against a purchase order →
the system matches it within a tolerance → the buyer approves or disputes it → on
approval (real or SLA-deemed), terms and due date lock permanently. **What it
deliberately doesn't do:** move money, assess credit, finance anything, or map an
authenticated caller to a specific buyer/supplier identity yet — see "What's
deliberately not built yet" below and `THREAT-MODEL.md` for the gaps this leaves open.

This file covers everything that supports the design: why this folder exists outside
the day-N structure, why modular monolith, how the dependency rule is enforced, how to
build and run it, what's deployed where, and what's deliberately not built yet.

## Why this lives at `capstone/`, not `day-22/task-2`

This is the Academy capstone: it starts at Day 22 (this kickoff) and continues through
Days 28–32 (build, polish, ship). It is one continuous project, not a new artifact each
day. Putting it under `day-22/task-2` would mean either duplicating it into every later
day's folder or quietly growing `day-22/task-2` past what was submitted for Day 22 —
so a mentor reviewing that folder later would find it further along than what was
actually turned in that day. Keeping it at the repository root avoids both: each day's
submission file (e.g. `submission-day-22-task-2.md`, and later
`submission-day-28-task-1.md` and so on) documents the state of `capstone/` *as of that
day*, without the code itself needing to move or fork. No other day-N folder is
touched by this work.

## Why a modular monolith, not microservices, for this slice

This slice is two bounded contexts (Procurement, Invoicing) with one synchronous
dependency between them (reserve/consume capacity) and no independent scaling,
deployment, or team-ownership need yet — it's a single developer proving a domain
model. Microservices would add network calls, serialization, and eventual consistency
to a boundary that currently needs to be *transactionally* consistent (PO capacity
must not be oversold, which is exactly why reservation is synchronous — see
DESIGN.md). Splitting now would be paying a distribution tax with none of
microservices' actual benefits.

The point of doing this as a *modular* monolith rather than a plain layered one is
that the module seam is real: folder structure, project structure, and the dependency
rule below all agree on where Procurement ends and Invoicing begins. If a real reason
to split out a service ever arrives (a team, a scaling need, a different release
cadence), the module boundary is already where the seam would need to be — the
Infrastructure adapter pattern below is what would become a network client.

## Dependency rule, and how it's enforced

Direction: `Domain ← Application ← Infrastructure`, with a thin `Web` host wiring
Infrastructure implementations to Application use cases at startup.

- **Domain** has no dependency on Application, Infrastructure, or the web framework —
  and `Invoicing.Domain` has no dependency on `Procurement` at all, in any layer, not
  even its published DTOs. It only depends on `Capstone.SharedKernel` (shared
  primitives: `Money`, `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`).
- **Application** depends only on its own module's Domain, and defines the interfaces
  (ports) it needs from the outside world — it does not depend on Infrastructure.
  `Invoicing.Application` defines `IPurchaseOrderCapacityPort` in its own vocabulary
  (a `PurchaseOrderReference`, not Procurement's `PurchaseOrderId`); it has no idea
  Procurement exists.
- **Infrastructure** implements those ports and is the only layer allowed to know
  about another module. `Capstone.Invoicing.Infrastructure` is the **one** project in
  the whole solution that references `Capstone.Procurement.Application` — via
  `ProcurementCapacityAdapter`, which translates Procurement's published
  `PurchaseOrderCapacitySnapshot` into Invoicing's own `PurchaseOrderSnapshot`. This is
  the dependency-inversion boundary: Invoicing defines the contract, Procurement's
  side of it is adapted to fit, not the other way round.
- **Web** (`Capstone.Web`) is the composition root: the only project allowed to
  reference every module's Infrastructure, because it's the one place that wires
  concrete implementations to interfaces via dependency injection.

This isn't just described — it's enforced two ways:

1. **Project references make the wrong direction impossible to compile.** No Domain
   `.csproj` references an Application or Infrastructure `.csproj`; no Application
   `.csproj` references an Infrastructure `.csproj`; `Capstone.Procurement.*` never
   references any `Capstone.Invoicing.*` project.
2. **`tests/Capstone.ArchitectureTests`** (using `NetArchTest.Rules`) asserts this at
   the assembly level via reflection, so a future contributor adding a `using` that
   *happens* to compile because of an already-present reference still fails a test.
   Five tests: Domain never depends on Application/Infrastructure/Web; Invoicing's
   Domain and Application never depend on Procurement in any form; Procurement never
   depends on Invoicing in any layer; Application never depends on Infrastructure or
   Web; and a positive check that `ProcurementCapacityAdapter` specifically *does*
   depend on `Capstone.Procurement.Application` — proving the one permitted crossing
   point actually exists, not just that nothing else crosses it. All five pass; see
   the submission file for the run output.

## Folder layout

```
capstone/
  DESIGN.md                          the one-page design
  README.md                          this file
  adr/                                architecture decision records (0001- onward)
  Capstone.slnx                      solution file (all 10 projects)
  src/
    SharedKernel/Capstone.SharedKernel/           Money, Entity, AggregateRoot, IDomainEvent
    Modules/
      Procurement/
        Capstone.Procurement.Domain/              PurchaseOrder aggregate
        Capstone.Procurement.Application/         ports + IPurchaseOrderCapacityGateway (published DTOs)
        Capstone.Procurement.Infrastructure/       in-memory repository
      Invoicing/
        Capstone.Invoicing.Domain/                Invoice aggregate (the core of this slice)
        Capstone.Invoicing.Application/           use cases + ports (IPurchaseOrderCapacityPort, IPaymentTermsLookup)
        Capstone.Invoicing.Infrastructure/         in-memory repository + ProcurementCapacityAdapter (the one cross-module reference)
  host/
    Capstone.Web/                    composition root; two demo endpoints proving the wiring resolves and runs
    Capstone.Worker/                 (Day 26) trace-demo worker - see infra/README.md; not a real async flow
  tests/
    Capstone.Invoicing.Domain.Tests/  25 tests against the Invoice aggregate's invariants
    Capstone.ArchitectureTests/       5 tests enforcing the dependency rule above
```

Each bounded context is a project group (`Domain`/`Application`/`Infrastructure`), not
just a namespace inside a shared project — that's what makes it *modular*, not merely
layered.

## Build and run

Requires the .NET 10 SDK (built and tested against SDK 10.0.302). All commands below
run from `capstone/`.

```
dotnet restore Capstone.slnx
dotnet build Capstone.slnx
dotnet test Capstone.slnx
```

To run the host and see the wiring execute end to end:

```
dotnet run --project host/Capstone.Web --urls http://localhost:5500
```

Then, in another terminal:

```
curl http://localhost:5500/
curl -X POST http://localhost:5500/demo/submit-sample-invoice
```

The second call seeds an in-memory purchase order and submits one invoice against it
through `SubmitInvoiceUseCase`, returning the generated PO and invoice IDs — proof the
full Procurement → Invoicing round trip actually runs, not just compiles. It's demo
scaffolding for this purpose only; it is not the deliverable and not a real API shape.

## Infrastructure

Bicep templates for this project's Azure infrastructure (API host, worker host,
SQL, Service Bus, Log Analytics, Application Insights) live at
[`infra/`](infra/), beside the application code they describe, for the same
reason the application itself lives at `capstone/` rather than a day-numbered
folder — see `infra/README.md` for what each module does, how dev and prod differ,
why Service Bus is provisioned ahead of the application's messaging code, and
(Day 26) how OpenTelemetry, App Insights, and the trace-demo worker fit together.
The reusable KQL queries and their captured results live at
[`observability/`](observability/).

## What's deployed, and where

Two live Azure environments, both `centralindia`, both left running (see
`infra/README.md` for the full topology and cost breakdown):

| | Dev | Prod |
|---|---|---|
| API | `https://capstone-dev-api-f7nsoj.azurewebsites.net` | `https://capstone-prod-api-kjritd.azurewebsites.net` |
| App Service plan | B1 Basic, 1 instance | S1 Standard, 2 instances |
| SQL | Serverless, free-limit | Standard S0/10 DTU (`GP_Gen5` unavailable on this subscription — see `infra/README.md`) |
| Network | VNet + private endpoint on SQL, public access disabled | same |
| Worker (trace demo) | `capstone-dev-worker-5yyaqy` | `capstone-prod-worker-pfstoz` |

Prod's infrastructure was originally stood up on 2026-09-15 (a Day-27-hardened
deployment, ahead of this day's own narrative — see `submission-day-32-task-1.md` for
why that gap exists and what was reconciled). Day 32 deployed the current, Day 31 app
code and its EF migrations onto that existing infrastructure, and brought its app
settings back in line with what `infra/modules/api.bicep` now declares (the two
Day 30 async-flow topic names, previously only ever applied out-of-band to dev).

**Both environments run on this subscription's Azure credit, which expires
2026-09-28.** After that date, both URLs stop resolving — this is expected, not a
sign the project was ever broken; see `submission-day-32-task-1.md` for the teardown
commands if you're reading this after expiry and want to reproduce the infrastructure
yourself.

## What's deliberately not built yet

This started (Day 22) as a design-and-scaffold kickoff; persistence (Day 29), auth
(Day 27), and both named async flows plus the dispute/withdraw/deemed-approval
surface (Day 30) have since landed - see each day's `submission-day-*.md` for what
shipped when. What's still genuinely open:

- **UI.** No frontend of any kind. The host endpoints exist to prove the flows work,
  not as a real API consumer experience.
- **Counterparty/Identity mapping.** Every endpoint still takes a raw, caller-supplied
  `Guid` for who's acting (`supplierId`, `approvingActorId`, ...) - see
  THREAT-MODEL.md's Spoofing/Elevation-of-privilege sections for the three findings
  this one gap causes at once.
- **Caching, resilience.** Not applicable yet - there's no external dependency (real
  database, real downstream service) worth caching or protecting against transient
  failure beyond what's already there. (Both were built for *other* Academy days,
  against real dependencies - see Day 21 Task 1 and Day 22 Task 1 - and are
  deliberately not carried into this greenfield project.)
- **A real Payment Terms bounded context.** Terms are a hardcoded default lookup
  (`InMemoryPaymentTermsLookup`, 45/10 days for every buyer-supplier pair) — see
  DESIGN.md for why this is a deliberate deferral, not an oversight, and what would
  need to change for it to become its own context.
- **Disputed-invoice expiry.** Day 30 closed this for abandoned `Submitted` invoices
  (the deemed-approval sweep now actually runs); a `Disputed` invoice nobody resolves
  still ties up PO capacity indefinitely, and deliberately has no automatic
  resolution - see DESIGN.md's "Purchase order capacity" section for why.
  `Invoice.Withdraw()` only applies to `Submitted`, not `Disputed` - a supplier stuck
  in an unresolved dispute cannot exit unilaterally either.
- **A real InvoiceApproved consumer.** The outbox and its relay (Day 30) publish to
  `invoice-approved-events`; nothing subscribes yet, because DESIGN.md names the
  Financing context this would feed as "boundary only" - not because the pipe is
  unfinished.
- **Matching tolerance as per-relationship configuration.** Currently a single global
  default (`MatchingPolicy.Default`), explicitly documented as a placeholder rather
  than a researched figure.
