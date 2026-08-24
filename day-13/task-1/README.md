# Day 13 Task 1 — Signals + zoneless + standalone

A standalone, zoneless Angular 21 app that fetches quotes from the real Week-1
`QuotesApi` contract and renders them with signals, `computed()`, and the new control
flow. Built by directing an agent (Claude Code) per the brief in `brief.md`, then
verified by reading the diff, running it, and mutating it. See `verification-log.md`
for the running log of what actually happened while building it, including three
genuine mistakes and how each was caught and fixed.

## What this is, structurally

- `src/app/quotes/quote.ts` — the `Quote` model, matching the real API DTO.
- `src/app/quotes/quote-api.ts` — `QuoteApi`, a service that calls `GET /api/quotes`.
- `src/app/quotes/quote-list/` — `QuoteList`, the standalone component described by
  the brief: two signals, a computed derived from both, `@for` with a real track key,
  `@if` for loading/empty, `@switch` for three view modes.
- `src/app/app.ts` / `app.html` — the root component, just hosts `<app-quote-list />`.
- `scripts/verify-structural.mjs` — plain Node script for the structural checks that
  can't run inside the Vitest test bundle (see verification log, Mistake 2).

## The real API contract this was built against

Read from `day-3/task-3/QuotesApi`, without modifying anything there:

- Route: `GET /api/quotes`, defined at
  `day-3/task-3/QuotesApi/Program.cs:361-362` as
  `app.MapGet("/api/quotes", (IQuoteRepository quotes) => Results.Ok(quotes.GetAll()));`
- DTO: `day-3/task-3/QuotesApi/Quotes/Quote.cs` —
  `public sealed record Quote(int Id, string OwnerId, string Text);`
- On the wire this is `{ id: number, ownerId: string, text: string }` — ASP.NET Core's
  Minimal API `Results.Ok(...)` serializes through the framework's default
  `Microsoft.AspNetCore.Http.Json.JsonOptions`, whose default `PropertyNamingPolicy` is
  `JsonNamingPolicy.CamelCase`; `Program.cs` overrides none of it (verified by
  grepping the file for `JsonSerializer`, `PropertyNamingPolicy`,
  `ConfigureHttpJsonOptions`, `JsonOptions` — zero matches).

One genuine, factual correction to the task's own framing: the brief assumes the whole
API sits behind JWT auth and can't be called unauthenticated. Reading the route table
shows `GET /api/quotes` specifically carries no `.RequireAuthorization()` — only the
mutation routes (`POST`/`PUT`/`DELETE /api/quotes`) and `GET /api/protected` do. This
is confirmed by `day-3/task-3/QuotesApi.Tests/AuthIntegrationTests.cs:189-208`, whose
anonymous-mutation test theory covers only `POST`/`PUT`/`DELETE`. This didn't change
what was built here — see interpretation 2 below — but it's worth recording accurately.

## Resolved ambiguities, in full

**1. Which API.** The Week-1 API is `day-3/task-3/QuotesApi`. This task only
*consumes* its contract; it never modifies it, so there's no conflict with treating
`day-3` through `day-12` as frozen. Every field name and the route above were read
directly from `Program.cs` and `Quotes/Quote.cs` and are quoted verbatim.

**2. Live calls versus contract fidelity.** No live authenticated call was made
against the real API, and no token was generated, hardcoded, or requested. The
service and component are tested with Angular's `HttpTestingController` and fixtures
whose field names come from the real DTO. This holds even though `GET /api/quotes`
turns out not to require auth (see above) — the point of the interpretation was to
avoid depending on a running server, Docker, or a credential in CI, and that still
applies regardless of which routes need a token.

**3. Two signals into one computed, concretely.** `quotes` (the fetched list) and
`filterText` (a user-controlled search string) are the two signals. `filteredQuotes`
is a `computed()` reading both — it filters `quotes()` by `filterText()`. Tests prove
it recomputes when `quotes` changes with `filterText` held constant, and vice versa
(`quote-list.spec.ts`). A third signal, `viewMode`, drives the `@switch` and does not
feed the computed — the brief only requires the computed to depend on "at least two"
signals, and it isn't required that every signal feed it.

**4. Testing.** The Angular CLI scaffolded Vitest (`@angular/build:unit-test`
builder, `vitest@^4.0.8` in `package.json`), not Karma — that's the Angular 21
default, and nothing else was added. All tests run offline against
`HttpTestingController`; none depend on the network, a running API, Docker, or a
credential.

**5. What zoneless changes.** Zone.js used to monkey-patch every async API
(`setTimeout`, `addEventListener`, XHR, Promises, …) so Angular could run a full
change-detection pass over the whole component tree whenever *anything* async might
have changed something — it had no way to know what, so it checked everything. With
signals, a template that reads a signal (e.g. `{{ filteredCount() }}`) registers that
specific view as a dependent of that specific signal. When the signal's value changes,
Angular schedules a refresh only for the views that actually read it — not the whole
tree, and not on a Zone.js hook firing. The practical consequence, demonstrated
directly in this component: mutating a plain class field from inside a `.subscribe()`
callback would do nothing visible now, because nothing is watching it — state has to
live in a `signal()` (as `quotes`, `filterText`, `viewMode`, `loading`, `error` all
do here) for a change to reach the DOM at all. In Angular 21, zoneless is the default
for new projects; `provideZonelessChangeDetection()` is not called anywhere in this
app (`app.config.ts`) and Zone.js is not a dependency (`grep -i zone.js package.json`
— no matches, also asserted by `scripts/verify-structural.mjs`).

**6. Field names.** Every field name in `quote.ts`, the service, the component, and
every test fixture is `id` / `ownerId` / `text`, taken from the real DTO in
interpretation 1 above — none were invented or guessed.

## Why `track` matters in `@for`

Without a track key, Angular's default behavior when the underlying array reference
changes is to tear down and recreate every DOM node for every item, because it has no
way to tell "this is the same quote, just re-ordered or re-filtered" from "this is a
brand new quote." That's wasted DOM work, and it destroys any per-item state (focus,
scroll position, CSS transition, form input) that lived in those nodes. `track
quote.id` tells Angular to key each rendered row to the real, stable identifier from
the API (`Id` in the DTO) rather than to array position or object identity — so when
`filterText` narrows the list, Angular reuses and reorders existing `<li>` elements
for quotes still present, instead of throwing all of them away. `@for` in Angular's
new control-flow syntax makes a track expression mandatory in the grammar itself (you
cannot omit it), which is why the mutation check in the verification log couldn't
literally "remove" it — it swapped the tracked field for a wrong one (`quote.text`)
instead, which is the realistic version of this mistake: a track key that compiles
fine but doesn't identify the object the way the real API's `id` does.

## Why `inject()` over constructor injection

`inject()` is called at class-field-initialization time, inside Angular's injection
context, which lets a dependency be assigned directly to a `readonly` field
(`private readonly http = inject(HttpClient);` in `quote-api.ts`) without a
constructor at all. Constructor injection needs a constructor parameter list that
grows with every dependency and needs to be repeated in every subclass; `inject()`
has no such requirement and works the same whether the value is used directly as a
field, computed from other injected values, or captured inside a factory function
passed to `provideAppInitializer` or a route guard — contexts where a class
constructor may not even exist. Nothing in this project uses constructor parameter
injection; `scripts/verify-structural.mjs` asserts this by scanning every non-spec
source file for the literal substring `constructor(`.

## Angular 21 vs. 22 pinning

The task names Angular 21, but Angular 22 (released after 21) is what
`npm install -g @angular/cli` installs by default as of this session — it moves
`OnPush` to the default change-detection strategy and raises the Node/TypeScript
floor. Installing 22 would have silently delivered defaults the task didn't ask for.
This project pins `@angular/cli@21` explicitly:
`npm install -g @angular/cli@21`, confirmed with `ng version` reporting
`Angular CLI: 21.2.21`. The scaffolded app depends on `@angular/core@^21.2.0` and
`typescript@~5.9.2` (from `package.json`), both real command output, not assumed.

## How to run and test

```bash
cd day-13/task-1
npm install            # already done; committed package-lock.json pins the tree
npx ng serve           # dev server — quotes will not load without a running QuotesApi
npx ng build           # production build
npx ng test --watch=false   # Vitest suite, no network/Docker/credentials required
node scripts/verify-structural.mjs   # structural checks (NgModule, inject(), track, Zone.js)
```

The real `QuotesApi` is not started as part of this task (see interpretation 2), so
`ng serve` will show the loading/error states without a backend running — that is
expected, not a bug.
