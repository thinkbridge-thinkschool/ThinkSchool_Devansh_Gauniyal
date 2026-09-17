# Day 30 Task 1 — Feature completeness

## PR

**PR URL:** _[fill in after opening — see "Opening the PR" below]_

**Review thread:** _pending — no reviewer feedback has come in yet. This section gets
filled in with real comments and my real responses once someone has actually looked at
the branch. Nothing here is invented._

### Opening the PR

`gh` isn't installed on this machine (and per this session's instructions, it doesn't get
installed). Steps to open it by hand:

1. Push already done by this session: branch `day-30/task-1` is on the `academy` remote.
2. Go to the repo on GitHub → you should see a "Compare & pull request" banner for
   `day-30/task-1`. If not, go to Pull requests → New pull request → base `main`,
   compare `day-30/task-1`.
3. Title: `Day 30 Task 1 — dispute resolution, deemed approval, and the real async flows`
4. Paste the description below into the PR body.
5. Open it as a normal PR (not a draft) — the branch is in a genuinely reviewable state.

### PR description to paste

> **What changed**
> Closes the four gaps Day 29 left open: correction linkage between a rejected invoice
> and its replacement, the two async flows DESIGN.md always described but nothing
> published to (an `InvoiceApproved` outbox, a best-effort supplier notification), HTTP
> endpoints for dispute/reject/withdraw (the use cases existed since Day 22, only the
> HTTP surface was missing), and a background sweep that fires deemed approval when a
> buyer takes no action inside the review window.
>
> **Why**
> Day 29 got the happy path (submit → approve) onto real persistence but left the rest of
> the lifecycle as domain logic nothing could reach over HTTP, and left both of
> DESIGN.md's async flows as prose with no wiring. This closes both gaps so every state
> in the lifecycle diagram is reachable and provable against real infrastructure, not
> just covered by a domain test.
>
> **How to test**
> - `dotnet test Capstone.slnx` — 46 tests (29 domain, 12 integration against real SQL
>   Server via Testcontainers, 5 architecture), all passing.
> - Run locally (`dotnet run --project host/Capstone.Web`) and walk the curl sequence in
>   this file's walkthrough section: submit → dispute → reject → submit a corrective
>   invoice referencing it → confirm the link persisted → confirm a non-Rejected invoice
>   can't be corrected → confirm a Disputed invoice can't be withdrawn → submit + withdraw
>   a clean invoice → trigger the manual deemed-approval sweep.
> - Against the deployed dev environment: the migration already applied and the app is
>   running with the new endpoints live (see verification log below for real Kudu output).
>
> **What to look at closely**
> - `OutboxRelayBackgroundService` and `DeemedApprovalSweepBackgroundService` are new
>   code inside `host/Capstone.Web`, not a repurposing of `host/Capstone.Worker`'s
>   `TraceDemoWorker` — reasoning is in the commit message and in code comments.
> - `Invoice.Withdraw()` still only allows withdrawal from `Submitted`, not `Disputed` —
>   a disputed invoice still ties up PO capacity until someone resolves it. This is a
>   deliberate, named gap, not an oversight — see "Deferred" below.
> - `SubmitInvoiceCommand.CorrectsInvoiceId` has no foreign-key constraint at the database
>   level — validated only at the application layer, in `SubmitInvoiceUseCase`.

### Self-review

Three things in this diff I'd expect a reviewer to flag, and how I'd defend each:

1. **The outbox relay and the deemed-approval sweep live in `Capstone.Web`, not
   `Capstone.Worker`.** A reviewer will reasonably ask why a background-processing
   concern isn't in the project literally named "Worker." My answer: `Capstone.Worker`
   today holds exactly one thing, `TraceDemoWorker`, which exists solely to give a
   distributed trace a second hop to stitch across — it's not "the async infrastructure
   project," it's a tracing demo that happens to be a worker. Moving today's two services
   there would mean deploying a second WebJob, wiring a second `IServiceScopeFactory`
   against the same DbContext, and getting no benefit for it, since `Capstone.Web`
   already has every dependency both services need, already deployed. If the project
   grows a third and fourth background service, that's the point to actually split
   background processing into its own host — not before there's a second real reason to.

2. **`Invoice.Withdraw()` doesn't allow withdrawal from `Disputed`.** Day 28's own
   self-critique flagged this exact gap — a disputed invoice can sit forever, holding PO
   capacity, if neither side acts. I looked hard at just allowing `Withdraw` from
   `Disputed` today and decided against it: `DESIGN.md`'s lifecycle diagram treats a
   dispute as something that needs bilateral resolution (buyer approves or rejects it),
   not something the supplier can unilaterally walk away from — letting the supplier
   withdraw mid-dispute would let them dodge a rejection that's already in motion. The
   right fix is a timeout on `Disputed` itself, not a new edge on `Withdraw`, and that's
   scoped as its own follow-up rather than bolted on today under time pressure.

3. **The manual `/v1/system/deemed-approval-sweep` trigger has no authorization narrower
   than "any authenticated caller."** It exists because a real review-window timer can't
   be waited out inside a working session, so the same use case is also exposed as an
   on-demand endpoint to prove it against real Azure SQL. Any authenticated caller — not
   just a scheduler or an admin — can currently fire it. That's the same class of gap
   `THREAT-MODEL.md` already names for other endpoints (no caller-to-party identity
   mapping yet), not a new one. It's safe today only because the use case itself is
   idempotent and side-effect-free for anything not actually past its review window — but
   it shouldn't ship to prod without a narrower role check.

### Commit log for the day

```
9328d6b Note that the two new Service Bus app settings were applied out-of-band
2492c4c Expose the two async-flow topic names as app settings
593635a Update DESIGN.md and README.md for what Day 30 actually shipped
f20c54c Expose dispute/reject/withdraw over HTTP, and start running the deemed-approval sweep and outbox relay
4d4fb0d Add the InvoiceApproved outbox and supplier notification, wired into approval and dispute
64898a6 Give corrective invoices a real link to what they correct
```

(Full messages are in the PR description's commit history — each is its own reviewable
piece: correction linkage + tolerance tests, outbox + notifier, HTTP surface + background
services, docs, Bicep outputs, out-of-band settings note.)

### Live project and snapshot

The live project is `capstone/` — `day-30/task-1/` is a frozen snapshot of it as this day
ended, never edited directly.

Commit representing this day's state: `264306023e43f540a3bd977884f4ee032c0edf3f`.

### Cut from Day 28's plan, and why

Day 28's self-generated plan named the `Counterparty/Identity` claims-to-party mapping
and per-caller filtering on `GET /v1/invoices` as "not cut" for Day 30 — explicitly
called out as a gap that shouldn't slip because it ships a known security hole
(`THREAT-MODEL.md`'s Spoofing, Information disclosure, and Elevation of privilege
findings all trace back to it). It did slip. Today's actual task brief asked specifically
for dispute resolution, tolerance enforcement, deemed approval, withdrawal/expiry, and
the real async flows over Service Bus — all four landed — and identity mapping wasn't
in that list. Between the two, I built what today was actually asked for. The gap is
still open, still named in `THREAT-MODEL.md`, and any authenticated caller can still see
and act on any invoice regardless of which party they represent. That's the single
biggest thing left undone against the Day 28 plan.

## What did you learn this session?
Submitting a partial-quantity corrective invoice against a PO line tripped the matching
tolerance automatically — I hadn't planned that, it just happened live during the curl
walkthrough, because matching compares against the PO line's full value, not a pro-rated
one. It was a good reminder that the tolerance check and the correction flow are two
separate rules that can collide in ways I hadn't designed for on paper.

## What would break this?
Two disputes landing on related invoices at nearly the same moment the sweep runs — the
sweep and a human dispute both write to the same invoice, and nothing today takes a lock
across that race. It's the same class of ordering risk as the deemed-approval-vs-dispute
race from Day 28, just on a different pair of writers.
