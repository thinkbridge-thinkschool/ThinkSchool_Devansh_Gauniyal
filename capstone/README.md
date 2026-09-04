# Capstone — build/run notes and the reasoning that didn't fit DESIGN.md

The one-page design (contexts, aggregate, invariants, state lifecycle, async flows) is
in [`DESIGN.md`](DESIGN.md). This file covers everything that supports it: why this
folder exists outside the day-N structure, why modular monolith, how the dependency
rule is enforced, how to build and run it, and what's deliberately not built yet.

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

## What's deliberately not built yet

Per the task: this is a design-and-scaffold kickoff, not a feature build. Later
Academy days (28–32) are where these get built:

- **Persistence.** Both repositories are in-memory (`ConcurrentDictionary`-backed).
  No database, no migrations, no EF Core.
- **Messaging / outbox.** DESIGN.md names two async flows (supplier notification,
  `InvoiceApproved` integration event via outbox) — neither is wired to a real queue
  or outbox table. `ApplyDeemedApprovalsUseCase` is a plain callable use case, not a
  background service or hosted timer; nothing currently calls it automatically.
- **UI.** No frontend of any kind. The two host endpoints exist only to prove the DI
  graph resolves.
- **Auth.** No authentication or authorization anywhere in the host.
- **Caching, resilience.** Not applicable yet — there's no external dependency (real
  database, real downstream service) worth caching or protecting against transient
  failure. (Both were built for *other* Academy days, against real dependencies —
  see Day 21 Task 1 and Day 22 Task 1 — and are deliberately not carried into this
  greenfield project.)
- **A real Payment Terms bounded context.** Terms are a hardcoded default lookup
  (`InMemoryPaymentTermsLookup`, 45/10 days for every buyer-supplier pair) — see
  DESIGN.md for why this is a deliberate deferral, not an oversight, and what would
  need to change for it to become its own context.
- **PO reservation expiry.** An abandoned `Submitted` invoice ties up PO capacity
  indefinitely. Named as a known gap in DESIGN.md, not solved here.
- **Matching tolerance as per-relationship configuration.** Currently a single global
  default (`MatchingPolicy.Default`), explicitly documented as a placeholder rather
  than a researched figure.
