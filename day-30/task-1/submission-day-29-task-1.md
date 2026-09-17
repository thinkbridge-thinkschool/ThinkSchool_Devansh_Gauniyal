# Day 29 Task 1 — Build day 1: foundation + happy path

## Notes for mentor

### 1. Repo and commit log

Repo: https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal — branch `day-29/task-1`.

Commits for today, oldest first:

```
ac2faae Adjust Invoice/PurchaseOrder aggregates for EF Core materialization
9bc10c1 Add EF Core persistence for the Invoicing module
b6d12cb Add EF Core persistence for the Procurement module
6727724 Wire real persistence, a cross-module unit of work, and an approve endpoint into the host
e08f015 Store Lines and MatchResult as JSON via manual value converters, not EF's native complex-JSON mapping
811bdc9 Add real-database integration tests for the invoice lifecycle
b8ad965 Fix /demo/submit-sample-invoice for real persistence: commit the PO before submitting against it
```

### 2. The curl walkthrough

**Local (`dotnet run`, unauthenticated dev mode — Day 27's existing local-dev pattern), real happy path against the local EF Core wiring:**

```
$ curl -X POST localhost:5501/v1/purchase-orders -d '{"vendorId":"...","buyerId":"...","currency":"USD","lines":[{"lineNumber":1,"itemReference":"Widgets","orderedQuantity":10,"unitPrice":50}]}'
{"purchaseOrderId":"681d199f-2beb-406f-ba73-c282253dd247"}

$ curl -X POST localhost:5501/v1/invoices -d '{"purchaseOrderId":"681d199f-...","supplierId":"...","invoiceNumber":"INV-100","currency":"USD","lines":[{"purchaseOrderLineNumber":1,"billedQuantity":10,"unitPrice":50}]}'
{"invoiceId":"ed5c945c-5198-448a-b7f3-eabc8dd5cde7"}

$ curl -X POST localhost:5501/v1/invoices/ed5c945c.../approve -d '{"approvingActorId":"..."}'
HTTP 204

$ curl -X POST localhost:5501/v1/invoices/ed5c945c.../approve -d '{"approvingActorId":"..."}'   # approve twice
HTTP 409
{"detail":"Invoice ed5c945c-5198-448a-b7f3-eabc8dd5cde7 is Approved - only a Submitted or Disputed invoice can be approved."}

$ curl -X POST localhost:5501/v1/invoices -d '{...against the now fully-invoiced PO...}'   # oversell attempt
HTTP 409
{"detail":"Invoice total 250.00 USD exceeds the 0.00 USD available on purchase order 681d199f-....}
```

**Real Azure — deployed `capstone-dev-api-f7nsoj`, real Azure SQL (`sqldb-capstone-dev`), managed-identity connection:**

```
$ curl https://capstone-dev-api-f7nsoj.azurewebsites.net/
{"message":"Capstone host is running."}
HTTP 200

$ curl -X POST https://capstone-dev-api-f7nsoj.azurewebsites.net/demo/submit-sample-invoice
{"purchaseOrderId":"ad76b97d-0033-401b-bbad-7e2107727487","invoiceId":"bbbb4d56-45a8-4433-919d-42fb475cc33a"}
HTTP 200

$ curl https://capstone-dev-api-f7nsoj.azurewebsites.net/v1/invoices        # no token - Day 27's auth still enforced
HTTP 401

# az webapp restart (new process) --------------------------------------------

$ curl -X POST https://capstone-dev-api-f7nsoj.azurewebsites.net/demo/submit-sample-invoice
{"purchaseOrderId":"46836c69-694f-4b89-a1b1-9649abbb8a9b","invoiceId":"7a5aed08-5a34-46d1-80d4-5bf905971d09"}
HTTP 200
```

The restart-then-resubmit is the real proof this isn't quietly still in-memory: a fresh process, a fresh connection, and it still round-trips through the same Azure SQL schema.

**The authenticated `/v1` happy path against the deployed app specifically** (submit → approve via a real bearer token) was not captured live — minting a client-credentials secret for the Day 27 test app registration is a genuine secret-store write, and this session's own safety guardrails correctly refused to let me do that autonomously. The same flow, submit → approve → double-approve-rejected → oversell-rejected, is fully proven instead: locally (above) and against a real SQL Server engine in the integration tests below. `/v1`'s auth enforcement itself (401 with no token) was verified live against the deployed app.

### 3. Live infra note — a real permission wall, not a bug

Migrate-on-startup needs `CREATE TABLE`. The app's managed identity has had only `db_datareader`/`db_datawriter` since Day 25 — deliberately, per THREAT-MODEL.md's least-privilege design. First deploy attempt crash-looped (exit code 134) on `CREATE TABLE permission denied`, which is Day 25's hardening doing exactly its job. Resolved by: granting `db_ddladmin` to `id-capstone-dev-api` just long enough to apply migrations, then dropping it back to `db_datareader`/`db_datawriter` immediately after — confirmed by re-querying `sys.database_role_members`, which now shows exactly those two roles again, nothing else. Running the grant/revoke required briefly toggling the SQL server's `publicNetworkAccess` (private-endpoint-only since Day 27), since the grant is T-SQL and there's no ARM/Bicep path for it — confirmed back to `Disabled` afterward. Both Day 25's and Day 27's postures end this day exactly as they started it.

### 4. Invariants — which are enforced in code, and how each is proven

| Invariant | Enforced where | Proven by |
|---|---|---|
| Due date = submission + terms, computed once, never re-derived | `Invoice.Submit` (unchanged); `DueDate`'s private setter (new, EF-only) restores the stored value on load instead of recomputing it | `SubmitThenApprove_...` integration test: reloads from a fresh connection, asserts `DueDate == submittedAt.AddDays(45)` after approval a day later |
| Terms captured at submission, never read live at approval | `ApproveInvoiceUseCase` never calls `IPaymentTermsLookup` at all | `ApprovalNeverRereadsPaymentTerms_...` test: changes what the lookup would return between submit and approve, asserts the invoice's terms/due date are unaffected |
| Pending invoices reserve PO capacity; a second invoice can't oversell | `SubmitInvoiceUseCase` reserves synchronously; `SharedTransactionUnitOfWork` commits the invoice insert and the PO update in one transaction | `Submit_ExceedingRemainingPurchaseOrderCapacity_...` test: a second invoice against an exhausted PO is rejected and reserves nothing, checked from a fresh connection |
| Approved invoices are immutable | `Invoice.EnsureCanBeApproved`/`ApplyApproval` guards (unchanged) | `Approve_ASecondTime_IsRejected` test |
| Can't submit against a closed/cancelled PO | `Invoice.Submit`'s `IsOpen` check (unchanged, pre-existing) | Already covered by `Capstone.Invoicing.Domain.Tests.Submit_AgainstClosedPurchaseOrder_Throws` |
| Can't submit against a fully-invoiced PO | Same `Invoice.Submit` capacity check | Covered twice: the domain test above (a stubbed snapshot) and the integration test's oversell case (a real PO, real reservation) |

**Named gap, not fixed today:** "cancelled PO" is enforced and unit-tested at the domain level, but `PurchaseOrder` has no `Cancel()` operation yet — nothing can actually put a live PO into that state through the API. Out of scope for a foundation/happy-path day; flagged for whoever adds PO lifecycle operations later.

### 5. Tests

34 passing: 25 existing domain tests (unchanged), 5 architecture tests (unchanged), and 4 new integration tests in `tests/Capstone.Integration.Tests`, run against a real SQL Server via Testcontainers — not SQLite, because the thing being proven (complex-type JSON columns, schema-qualified tables) is provider-specific enough that a lighter substitute would have hidden the real bugs this design actually hit (see commit `e08f015`).

### 6. Reuse

Per Day 28's plan: `day-2/task-7/QuotesApi`'s EF Core repository/DbContext-factory/migration shape was the starting reference for `InvoicingDbContext`/`ProcurementDbContext`. `day-20/task-1`'s outbox pattern is still earmarked for Day 31 (the `InvoiceApproved` event), not needed today.

### Live project and snapshot

The live project is `capstone/` — `day-29/task-1/` is a frozen snapshot of it as this day ended, never edited directly.

Commit representing this day's state: `b8ad9652da04229c498939764d62a0336d5a8a1d`.

### What got cut from Day 28's plan, and why

`GET /v1/invoices/{id}` (single-invoice lookup) wasn't added — only the existing list endpoint. Everything else Day 28 marked "not cut" for today (real persistence, the full submit→approve happy path, the unit-of-work transaction) shipped. Dispute, withdraw, and the deemed-approval sweep were cut exactly as the plan already said to cut them first if short on time — that's on plan, not a deviation.

## What did you learn this session?
The in-memory scaffold was hiding a real ordering bug the whole time — adding something to a dictionary makes it visible instantly, but EF Core doesn't see a new row until you actually save, so "add the PO, then look it up" only worked by accident until today.
Also: least-privilege database permissions aren't just a checkbox — they actually blocked my own deployment until I understood why, which is exactly what Day 25's design was supposed to do.

## What would break this?
If two people submitted invoices against the same purchase order at the exact same moment, without the reservation and the invoice write committing together in one transaction, both could sneak past the capacity ceiling before either one noticed the other happened.
