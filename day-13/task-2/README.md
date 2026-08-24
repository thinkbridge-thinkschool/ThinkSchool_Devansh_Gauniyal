# Day 13 Task 2 — A real component from a spec

A standalone, zoneless Angular 21 quotes list-plus-detail component built against the
real Week-1 `QuotesApi` contract, with a working, test-proven stale-response guard and
strict TypeScript throughout — no `any` anywhere. Built by directing an agent (Claude
Code) per the brief in `brief.md`, then verified by reading the diff, running it, and
mutating it. See `verification-log.md` for the running log of what actually happened,
including one genuine mistake and two deliberate mutations, kept clearly separate.

## What this is, structurally

- `src/app/quotes/quote.ts` — the `Quote` model, matching the real API DTO.
- `src/app/quotes/quote-api.ts` — `QuoteApi`, a service calling the real `GET
  /api/quotes` for both the list and (by re-calling it and filtering client-side) the
  detail, with a header comment naming the exact endpoint, fields, and source files.
- `src/app/quotes/quote-browser/` — `QuoteBrowser`, **one component with both the list
  and detail panes** (see "One component or two", below).
- `src/app/app.ts` / `app.html` — the root component, hosts `<app-quote-browser />`.
- `scripts/verify-structural.mjs` — plain Node script for structural checks that can't
  run inside the Vitest test bundle (browser-targeted compiler, no Node built-ins).

## The real API contract this was built against

Re-read from `day-3/task-3/QuotesApi` for this task (not trusted from Task 1's notes),
without modifying anything there:

- Route: `GET /api/quotes`, at `day-3/task-3/QuotesApi/Program.cs:361-362` —
  `app.MapGet("/api/quotes", (IQuoteRepository quotes) => Results.Ok(quotes.GetAll()));`
- DTO: `day-3/task-3/QuotesApi/Quotes/Quote.cs` —
  `public sealed record Quote(int Id, string OwnerId, string Text);`
- Wire shape: `{ id: number, ownerId: string, text: string }` — ASP.NET Core's Minimal
  API default JSON naming policy is camelCase, and `Program.cs` overrides none of it
  (`grep -n "JsonSerializer\|PropertyNamingPolicy\|ConfigureHttpJsonOptions\|JsonOptions" Program.cs`
  — zero matches). This is no longer just a source-reading deduction: Day 13 Task 1
  ran the real API locally and confirmed it live — the unauthenticated `GET
  /api/quotes` returned exactly `{"id":1,"ownerId":"user-1","text":"Security is a
  process."}`.
- **No per-item detail route exists.** The full route table
  (`grep -n "app.Map" Program.cs`) has exactly one `GET` with no path parameter
  (`/api/quotes`) and two routes with a path parameter, both mutations and both
  auth-gated: `PUT /api/quotes/{id:int}` and `DELETE /api/quotes/{id:int}`. There is no
  `GET /api/quotes/{id}`.

## Resolved ambiguities, in full

**1. Which API.** `day-3/task-3/QuotesApi`, re-verified against the source directly
for this task rather than trusting Task 1's recorded field names — they matched.

**2. List and detail endpoints.** No separate detail route exists (see above). Per the
task's own suggested handling, `QuoteApi.getQuoteDetail(id)` re-calls the one real read
endpoint, `GET /api/quotes`, and resolves the requested item from the response
client-side (`quote-api.ts`). This was a deliberate choice over synchronously indexing
into the already-loaded list: a synchronous lookup has no async gap at all, so the
"race" the brief asks for would be purely theoretical. Re-issuing a real HTTP request
per selection keeps the race genuine — two independently-timed round trips that really
can resolve out of order — while still never inventing a route the API doesn't have.

**3. Live calls versus contract fidelity.** No live authenticated call was made, no
token generated or hardcoded. The service and component are tested with
`HttpTestingController` and fixtures whose field names come from the real DTO. This
holds regardless of `GET /api/quotes` itself needing no auth (see above) — the point
is not depending on a running server or credential in CI.

**4. The stale-response race.** `QuoteBrowser.selectQuote(id)` captures `id` in the
closure passed to `.subscribe()`. When that response arrives, it compares
`this.selectedId() !== id` — if a later `selectQuote()` call has since moved the
selection on, the response is discarded (`return` before touching any signal). This
is invisible on a fast local network because both requests usually resolve in
issue-order anyway; it becomes a real bug the moment two responses for the same
resource can resolve in a different order than they were sent — a slow request racing
a fast one, a retried request landing after a fresh one, or (as tested here) two
requests to an endpoint whose latency has no guaranteed ordering. The guard is proven
by `quote-browser.spec.ts`'s RACE test, which selects id 1, then id 2, then flushes id
2's request first and id 1's *last* — proving the pane shows id 2, not whichever
response merely arrived last.

**5. No `any`.** `tsconfig.json` has `"strict": true`, which implies `noImplicitAny`
(confirmed directly via `npx tsc --showConfig -p tsconfig.app.json`, which resolves
`noImplicitAny: true`). `scripts/verify-structural.mjs` additionally greps every `.ts`
file — including spec files — for `: any`, `as any`, `<any>`, and `any[]`. This matters
specifically for this task because `any` is exactly the mechanism by which a wrong or
guessed field name would survive compilation: with `Quote` fully typed and no `any`
escape hatch anywhere, `quotes.find(q => q.id === id)` and every signal write are
type-checked against the real DTO shape, so a mismatch is a compile error, not a
runtime `undefined` discovered by a user. (This *is* how it went, in fact — see
`verification-log.md`: the first test file used `as any` to reach protected component
state, and the structural check caught it before it could hide a real mismatch
somewhere else.)

**6. Errors must not be swallowed.** Both the list and detail requests set their error
signal, and explicitly do *not* set the corresponding data signal to an empty value, on
failure. Proven by two tests: a failing list request leaves `listData()` `null` (not
`[]`) and renders `[data-testid="list-status-error"]` instead of
`[data-testid="list-status-empty"]`; the mutation check demonstrates the failure mode
directly by swallowing the error into `listData.set([])` and watching both of those
assertions fail for real.

**7. Field names.** Every field name in `quote.ts`, the service, the component, and
every test fixture is `id` / `ownerId` / `text`, taken from the real DTO above — none
invented or guessed.

## One component or two

**One component, `QuoteBrowser`, holding both the list and detail panes.** The
selection interaction ties them together tightly: clicking a list row needs to read
and write the same `selectedId` signal that the detail-loading logic and the
stale-response guard both depend on. Splitting this into two components would mean
passing `selectedId` down as an input and emitting selection events back up, plus
somewhere to own the guard — extra wiring for no benefit, since nothing here needs the
list and detail to be independently reusable or independently rendered elsewhere.

## Why a swallowed error is worse than a loud one

A `catchError` (or, as in the mutation check, an `error` callback) that turns a failed
request into `[]` makes a broken backend indistinguishable from a backend that
legitimately has zero quotes. The user sees "no quotes yet" and has no signal that
anything is wrong — no retry affordance, no indication to check their connection or
report a bug. A visible error state is strictly more informative even though it's
less pleasant to look at.

## Why `track` matters in `@for`

Without a stable track key, Angular has to assume every item in a re-rendered list
might be new, and tears down and rebuilds the DOM for all of them — losing whatever
state lived in those nodes (focus, in-progress input, CSS transition) and doing
unnecessary work. `track quote.id` ties each rendered row to the real, stable
identifier from the API, so Angular can tell "this is the same quote" from "this is a
new one" across re-renders.

## Angular 21 pinning

Re-verified rather than assumed: `ng version` reports `Angular CLI: 21.2.21`, same
major version as Task 1, avoiding Angular 22's changed defaults (`OnPush` becoming the
default change-detection strategy, a higher Node/TypeScript floor).

## How to run and test

```bash
cd day-13/task-2
npm install                  # already done; committed package-lock.json pins the tree
npx ng serve                  # dev server — quotes will not load without a running QuotesApi
npx ng build                  # production build
npx ng test --watch=false     # Vitest suite, no network/Docker/credentials required
node scripts/verify-structural.mjs   # structural checks (NgModule, inject(), any, track, Zone.js)
```
