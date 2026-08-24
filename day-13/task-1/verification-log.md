# Verification log — Day 13 Task 1

Kept in order, as work happened. Not reconstructed afterward.

## 2026-08-24 10:11 IST — Reading the real API contract

Read, without modifying, the following files under `day-3/task-3/QuotesApi`:

- `Program.cs` — route table. The relevant route is a Minimal API endpoint, not a
  controller class: `app.MapGet("/api/quotes", (IQuoteRepository quotes) => Results.Ok(quotes.GetAll()));`
  at `day-3/task-3/QuotesApi/Program.cs:361-362`. No `.RequireAuthorization()` is
  attached to this specific route (mutations — `POST`/`PUT`/`DELETE` on `/api/quotes` —
  and `GET /api/protected` do carry `.RequireAuthorization()`; the read route does not).
  This is confirmed by the test at `day-3/task-3/QuotesApi.Tests/AuthIntegrationTests.cs:189-208`,
  whose anonymous-mutation theory covers `POST`/`PUT`/`DELETE` only, never `GET /api/quotes`.
- `Quotes/Quote.cs` — the response DTO: `public sealed record Quote(int Id, string OwnerId, string Text);`
- `Quotes/QuoteRequests.cs` — request DTOs (`CreateQuoteRequest(string Text)`,
  `UpdateQuoteRequest(string Text)`), not used by this task since only `GET /api/quotes` is consumed.
- `Quotes/IQuoteRepository.cs` and `Quotes/InMemoryQuoteRepository.cs` — confirm `GetAll()`
  returns `IReadOnlyCollection<Quote>` ordered by `Id`, seeded with two quotes
  (`user-1`/"Security is a process.", `user-2`/"Policies make intent explicit.").
- `Authentication/AuthenticationSchemes.cs` — confirms the dual-scheme setup named in the
  task (`InternalJwt`, `EntraId`, plus a `SmartBearer` selector), which is why no live
  authenticated call is being attempted for the mutation routes.

**Wire casing decision.** The `Quote` record's C# properties are `Id`, `OwnerId`, `Text`
(PascalCase). Program.cs registers no custom `JsonSerializerOptions`, no
`ConfigureHttpJsonOptions`, and no naming-policy override anywhere in the file (checked with
`grep -n "JsonSerializer\|PropertyNamingPolicy\|ConfigureHttpJsonOptions\|JsonOptions" Program.cs`,
zero matches). ASP.NET Core's Minimal API `Results.Ok(...)` serializes through the framework's
default `Microsoft.AspNetCore.Http.Json.JsonOptions`, whose default `PropertyNamingPolicy` is
`JsonNamingPolicy.CamelCase` — this is a documented ASP.NET Core framework default, not a
project-specific setting, and not something I am guessing about this specific API's data. On
the wire this means the real field names are `id`, `ownerId`, `text`. This is stated here as a
framework-default deduction from reading the source, not from a live call (none was made — see
README interpretation notes). If it is wrong, the Angular service's fixture-based tests
(built from these exact names) are exactly what would catch it against a real server.

Genuine finding, not a mistake — worth flagging: the task brief text (and Devansh's own
`day-3/task-3` work) frames the whole API as sitting behind JWT auth. Reading the actual route
table shows `GET /api/quotes` specifically has no `.RequireAuthorization()` — only the
write routes do. This doesn't change what was built (no live call was attempted regardless,
per the interpretation notes), but it is worth recording accurately rather than silently
going along with the (slightly imprecise) premise.

**Real contract used going forward:**
- Endpoint: `GET /api/quotes`
- Response: JSON array of `{ id: number, ownerId: string, text: string }`

## 2026-08-24 10:19 IST — First `ng test` run: two genuine compile failures

After writing `quote-api.spec.ts`, `quote-list.spec.ts`, `app.spec.ts`, and a first
attempt at structural checks (`src/app/structural.spec.ts`), running `npx ng test
--watch=false` for the first time failed to build, with real esbuild/TypeScript errors
(captured in full at `output/angular-test-run-1.txt`):

```
✘ [ERROR] TS2349: This expression is not callable.
  Type 'TestContext' has no call signatures. [plugin angular-compiler]
    src/app/quotes/quote-api.spec.ts:47:6:
      47 │       done();

✘ [ERROR] TS2307: Cannot find module 'node:fs' or its corresponding type declarations.
    src/app/structural.spec.ts:1:52:
      1 │ import { readFileSync, readdirSync, statSync } from 'node:fs';
```

**Mistake 1 — wrong assumption about the test runner's async API.** I wrote the two
"fixture" tests in `quote-api.spec.ts` using a Jasmine/Karma-style `(done) => { ...
done(); }` callback. Angular 21's default test runner is Vitest (confirmed from
`package.json`: `"vitest": "^4.0.8"`, builder `@angular/build:unit-test`), not Karma —
Vitest's `it(name, fn)` passes the sole argument as a `TestContext`, not a legacy
`done` callback, so calling it as a function is a type error. Fix: since
`HttpTestingController.flush()` resolves synchronously, I removed `done` entirely and
captured the emitted value in a local variable, asserting after `.flush(...)` — no
callback needed. Confirmed fixed by re-running the suite (`output/angular-test-run-2.txt`):
13/13 tests pass.

**Mistake 2 — wrong assumption about the test bundle's runtime.** I assumed the spec
files compiled by `@angular/build:unit-test` run in a plain Node process and could use
`node:fs`/`node:path` to read source files for structural checks (no-NgModule,
no-constructor-injection, track-expression-present, no-Zone.js). The real error shows
spec files are compiled through the same browser-targeted `angular-compiler` esbuild
plugin as `ng build`, which has no Node built-ins available. Fix: moved the structural
checks out of the Vitest suite entirely into a standalone Node script,
`scripts/verify-structural.mjs`, run directly with `node scripts/verify-structural.mjs`
(not through the Angular build pipeline). This is also a more conventional home for
static source-text checks than a browser-run unit test.

**Mistake 3 — the fix for Mistake 2 shipped with its own bug.** The first version of
`verify-structural.mjs` used the regex `/@for\s*\([^)]*track\s+quote\.id[^)]*\)/` to
check for a `track` expression on the `@for` block. Running it for real
(`output/structural-check-1.txt`) reported `FAIL: the @for block has a track
expression` even though the block plainly has one:
`@for (quote of filteredQuotes(); track quote.id) {`. Cause: the character class
`[^)]*` (no closing parens allowed) breaks on the `)` inside `filteredQuotes()`, which
appears *before* `track` in the same expression — so the regex never reaches
`track quote.id`. Fix: changed the character class to `.*` (`/@for\s*\(.*track\s+quote\.id.*\)\s*\{/`),
which tolerates the nested `)`. Re-run (`output/structural-check-2.txt`): all 5
structural checks pass.

None of these three were deliberately introduced — they were the actual first-attempt
output, left in place until the real tool output showed they were wrong, exactly as
they happened.

## 2026-08-24 10:22 IST — Mutation check (deliberate, required by the task — not the bug above)

These two breaks were introduced on purpose to prove the tests actually test something.
They are separate from the three genuine mistakes logged above; both were reverted
immediately after capturing the failing output.

**Mutation 1 — core proof: wrong `track` key.** Changed the "list" view's `@for` in
`quote-list.html` from `track quote.id` to `track quote.text` (real output:
`output/mutation-A-broken.txt`, `output/mutation-A-broken-v2.txt`). First attempt at
the check passed when it should have failed — see Mistake 4 below. After fixing the
check, running `node scripts/verify-structural.mjs` reported:
```
FAIL: every @for block tracks by the real identifier field (quote.id)
      @for (quote of filteredQuotes(); track quote.text) {
```
Reverted `quote-list.html` from the pre-mutation backup, diffed identical to the
original, then re-ran: all 5 structural checks pass (`output/mutation-A-reverted.txt`).

**Mistake 4 — the structural check itself was too weak, caught by its own mutation
test.** The original `track`-expression regex matched against the whole file, so with
three `@for` blocks and only one mutated, the still-correct two blocks made the check
pass anyway (`output/mutation-A-broken.txt` shows a false "PASS"). Fixed by matching
each `@for (...) {` block individually and requiring `track quote.id` in every one
(`scripts/verify-structural.mjs`). Re-running the same mutation then correctly failed
(`output/mutation-A-broken-v2.txt`), naming the exact offending block.

**Mutation 2 — computed stops depending on the second signal.** Changed
`filteredQuotes` in `quote-list.ts` to ignore `filterText()` entirely (just
`return this.quotes()`). Running `npx ng test --watch=false` failed exactly the test
that exercises this edge (`output/mutation-B-broken.txt`):
```
AssertionError: expected 2 to be 1 // Object.is equality
 ❯ src/app/quotes/quote-list/quote-list.spec.ts:73:56
```
Reverted `quote-list.ts` from the pre-mutation backup, diffed identical to the
original, then re-ran: 13/13 tests pass again (`output/mutation-B-reverted.txt`).

## Summary of states and edges actually exercised

- Loading state (`@if` loading branch) before the fetch resolves.
- Empty state (`@if` empty branch) with zero quotes, list not rendered.
- Populated state: one `<li>` per fetched quote.
- The computed (`filteredQuotes`) recomputing when the first signal (`quotes`) changes
  with the second (`filterText`) held constant, and vice versa.
- All three `@switch` branches (`list`, `compact`, `ids-only`), each rendering its own
  distinct DOM.
- The service parsing a fixture built from the real DTO field names
  (`id`, `ownerId`, `text`).
- The service failing to populate `ownerId` from a fixture using a wrong field name
  (`owner`), proving the binding is to the real contract, not a coincidence.
- Structural facts: no `NgModule`, no constructor-parameter injection, every `@for`
  tracks by the real `id` field, no `Zone.js` reference anywhere in the project config.

**What would break if the API contract changed:** if `QuotesApi.Quotes.Quote` ever
renamed `OwnerId` to something else, or the JSON naming policy stopped being camelCase,
the `Quote` interface in `quote.ts` would no longer match the wire shape. Nothing here
would fail to compile — `HttpClient.get<Quote[]>` does not validate its response at
runtime — so `ownerId` would just come back `undefined` in the browser, silently,
exactly as reproduced by the wrong-field-name fixture test above. The wrong-field-name
test is the thing that would need to fail against a real server for this to be caught
before a user notices a blank field in the UI.
