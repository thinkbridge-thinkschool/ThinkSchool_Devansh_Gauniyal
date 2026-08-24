# Verification log — Day 13 Task 2

Kept in order, as work happened. Not reconstructed afterward.

## 2026-08-24 12:45 IST — Reading the real API contract (re-verified, not trusted from Task 1)

Re-read `day-3/task-3/QuotesApi/Program.cs` directly rather than trusting Task 1's notes.
Full route table (`grep -n "app.Map" Program.cs`):

```
app.MapGet("/", ...)
app.MapGet("/api/protected", ...).RequireAuthorization()
app.MapPost("/api/auth/login", ...)
app.MapPost("/api/auth/refresh", ...)
app.MapGet("/api/quotes", (IQuoteRepository quotes) => Results.Ok(quotes.GetAll()))
app.MapPost("/api/quotes", ...).RequireAuthorization(AuthorizationPolicies.CanEditQuotes)
app.MapPut("/api/quotes/{id:int}", ...).RequireAuthorization(AuthorizationPolicies.CanEditQuotes)
app.MapDelete("/api/quotes/{id:int}", ...).RequireAuthorization()
app.MapGet("/api/authors/quote-summary", ...)   // Day 11 Task 1, different domain, not used here
```

**Genuine finding, resolving interpretation 2:** there is no `GET /api/quotes/{id}` or any
other per-item read route. The only routes with a path parameter are `PUT` and `DELETE`
(mutations, both auth-gated). `GET /api/quotes` (the list) is the only read route this task
can legitimately call. This is the same conclusion Task 1 reached independently, and I
re-verified it here rather than assuming it still held.

**How this was handled (per interpretation 2's own guidance — not inventing a route):** the
"detail" fetch calls the real `GET /api/quotes` endpoint again, then resolves the selected
quote from the response by matching `id`. This is not fabricating a `/api/quotes/{id}`
endpoint — it's a second call to the one real read endpoint that exists — and it keeps the
race genuine (a real, independently-timed HTTP round trip per selection) rather than a
synchronous lookup into the already-loaded list, which would make the "race" theoretical.

DTO, from `day-3/task-3/QuotesApi/Quotes/Quote.cs`:
`public sealed record Quote(int Id, string OwnerId, string Text);`

Wire casing: camelCase (`id`, `ownerId`, `text`). Re-confirmed no custom
`JsonSerializerOptions`/`ConfigureHttpJsonOptions`/naming-policy override anywhere in
`Program.cs` (`grep -n "JsonSerializer\|PropertyNamingPolicy\|ConfigureHttpJsonOptions\|JsonOptions"` — zero matches), same as Task 1's finding, same reasoning: ASP.NET Core's
Minimal API default naming policy is camelCase, and nothing here overrides it. This was
independently confirmed live in Task 1 by actually running the API locally and hitting the
unauthenticated `GET /api/quotes` — it returned exactly `{"id":1,"ownerId":"user-1","text":"Security is a process."}` — so this isn't just a source-reading deduction anymore, it's
been observed directly, in this same repository, against this same code.

Auth: `GET /api/quotes` itself carries no `.RequireAuthorization()` (only the write routes
and `/api/protected` do) — same as Task 1 found. This task still makes no live authenticated
call (see interpretation 3) — the point of that interpretation is not depending on a running
server/credential in CI, which holds regardless of which specific routes need a token.

**Real contract used going forward:**
- List: `GET /api/quotes` → `Quote[]`, `{ id: number, ownerId: string, text: string }`
- Detail: the same `GET /api/quotes`, filtered client-side to the selected `id` — no
  separate detail route exists.

## 2026-08-24 12:50 IST — Genuine mistake: `as any` in the test file, caught by my own structural check

Wrote `quote-browser.spec.ts` accessing the component's `protected` state signals via
`(component as any).listLoading()`, `(component as any).selectQuote(1)`, etc. — 17
occurrences. This is the exact pattern used in Day 13 Task 1's tests, which was fine
there because that task's structural check only banned `any` in non-spec files and
never banned `any` at all. This task's brief is stricter and explicit: "no file contains
`: any`, `as any`, `<any>` or `any[]`" — no carve-out for spec files. I carried over the
Task 1 pattern without re-reading this task's stricter rule, and `node
scripts/verify-structural.mjs` caught it immediately on the first real run
(`output/structural-check-1.txt`):

```
FAIL: no `any` anywhere (`: any`, `as any`, `<any>`, `any[]`)
      .../quote-browser.spec.ts: expect((component as any).listLoading()).toBe(true); | ...
      [17 occurrences total]
```

**Fix:** changed the component's state signals and `selectQuote` from `protected
readonly`/`protected` to `public readonly`/`public` in `quote-browser.ts`. Angular's
template type-checker only requires members bound in the template to be `protected` or
`public`, so this is a legitimate visibility choice, not a hack — and it means the spec
file can call `component.listLoading()`, `component.selectQuote(1)`, etc. directly, with
no cast of any kind. Re-ran `node scripts/verify-structural.mjs`: all 7 checks pass
(`output/structural-check-2.txt`). Re-ran `npx ng test --watch=false` after the rename:
still 15/15 (`output/angular-test-run-2.txt`).

## 2026-08-24 12:52 IST — Mutation check (deliberate, required by the task — not the bug above)

Two breaks introduced on purpose to prove the tests test something real. Both reverted
immediately after capturing the failing output. Kept separate from the genuine `any`
mistake above.

**Mutation 1 — core proof: remove the stale-response guard.** Deleted the
`if (this.selectedId() !== id) { return; }` checks from both the `next` and `error`
callbacks in `selectQuote()`. Ran `npx ng test --watch=false`
(`output/mutation-A-broken.txt`): the RACE test failed exactly as it should —
```
AssertionError: expected 1 to be 2 // Object.is equality
```
With the guard gone, A's late response unconditionally overwrites B's, so the pane
shows quote 1 (A) instead of quote 2 (B) — precisely the bug the guard exists to
prevent. Reverted from the pre-mutation backup, diffed identical to the original, then
re-ran: 15/15 pass again (`output/mutation-A-reverted.txt`).

**Mutation 2 — swallow the list error.** Changed the list request's `error` callback
from `this.listError.set(...)` to `this.listData.set([])` — turning a failed request
into an apparently-successful empty result, the exact "swallowed error" failure mode
named in the task. Ran the suite (`output/mutation-B-broken.txt`): both ERROR tests
failed —
```
ERROR: a failing list request sets listError and leaves listData unset, not an empty success
  AssertionError: expected null to be truthy   (listError() was still null)
ERROR: the list error state renders and is distinguishable from the empty state
  AssertionError: expected null to be truthy   ([data-testid="list-status-error"] never rendered)
```
Reverted from the pre-mutation backup, diffed identical to the original, then re-ran:
15/15 pass again (`output/mutation-B-reverted.txt`). Final structural check after both
mutations were reverted: 7/7 pass (`output/structural-check-final.txt`).

## Summary of states and edges actually exercised

- LOADING: `listLoading()`/`detailLoading()` true in flight, false after settling, for
  both list and detail (two dedicated tests).
- ERROR: a failing list or detail request sets the respective error signal and leaves
  the corresponding data signal `null` — never an empty success — and the error branch
  renders distinguishably from the empty branch in the DOM.
- EMPTY: a successful zero-item list response renders the empty branch, not the list
  or the error branch.
- RACE: two overlapping detail requests (select id 1, then id 2, before the first
  resolves) flushed out of order — id 2's request flushed first, id 1's flushed last —
  and the detail pane ends up showing id 2, not id 1.
- Contract: the service parses a fixture built from the real DTO field names
  (`id`, `ownerId`, `text`) and fails to populate `ownerId` from a fixture using the
  wrong name (`owner`).
- Structural facts: no `NgModule`, no constructor-parameter injection, no `any` of any
  form anywhere in the project (including test files), `strict`/`noImplicitAny` both
  on, every `@for` tracks by the real `id` field, no `Zone.js` reference anywhere.

**What breaks if the Week-1 API contract changes:** if `QuotesApi.Quotes.Quote` ever
renamed `OwnerId`, `HttpClient.get<Quote[]>` would not fail to compile or throw at
runtime — it doesn't validate the response shape — `ownerId` would just come back
`undefined` silently in the browser, exactly as reproduced by the wrong-field-name
fixture test. Separately, the stale-response guard only compares against `selectedId()`
— it says nothing about the *list* request racing against itself (e.g. two rapid
re-fetches of `GET /api/quotes` landing out of order), which is a different kind of
interleaving this guard does not cover.
