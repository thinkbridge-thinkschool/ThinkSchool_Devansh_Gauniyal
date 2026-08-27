# Day 16 Task 1 — Routing, lazy loading, guards

Replicates `day-15/task-1` (commit `f1c2121b43ea269079fcc5030b4f3bc0bbcee029`) unchanged
into this folder, then adds routing, a lazy-loaded detail route, a functional auth
guard, route-param handling, and a View Transition. See `PROVENANCE.md` for the exact
file-by-file diff and `verification-log.md` for what actually went wrong while building
it and how each issue was caught.

## What lazy loading is, and why a stray eager import defeats it silently

`loadComponent: () => import('./quotes/quote-detail-page/quote-detail-page').then(m =>
m.QuoteDetailPage)` in `app.routes.ts` tells the Angular/esbuild build pipeline that
`QuoteDetailPage` belongs in its own chunk, fetched only when the router actually
activates a route pointing at it — not downloaded, parsed, or executed on first page
load. The saving is real: this app's lazy chunk is 3.56 kB raw / 1.22 kB over the wire
(`output/lazy-load-proof.md`), none of which the user pays for unless they navigate to
`/quotes` or `/quotes/:id`.

The danger is that a `loadComponent()` route can still end up compiled into the main
bundle anyway, with no error and no warning, if anything else in the app statically
imports the same component — a stray import left in a shared file, a barrel export, a
type-only import written without the `type` keyword, or (as reproduced deliberately in
this task's mutation check) simply forgetting and importing it for an unrelated reason.
The bundler sees a static import path and an eager one and just satisfies both by
bundling it eagerly — the `loadComponent()` call still "works" in the sense that
`import()` still resolves, but the code was already sitting in the main bundle the whole
time, so nothing was actually saved. Nothing in the source *looks* wrong; only the build
output tells you. That is why this task's proof is a build (`output/lazy-load-proof.md`)
and a grep for a string unique to the detail component's template, not a claim, and why
`scripts/verify-structural.mjs` now has a static check for exactly this (see the
"quote-detail-page is never statically imported outside its own directory" check, and
Mutation B in `verification-log.md`, which reproduced the failure on purpose and showed
the detail component's template string landing in an *initial* chunk instead of the lazy
one).

## What a functional guard is, and why UrlTree beats navigate

`auth.guard.ts` is a `CanActivateFn` — a plain function, not a class, registered directly
on a route's `canActivate` array (`app.routes.ts`). It reads
`AuthTokenService.getToken()` — the same `devAuthToken` localStorage-backed token source
`auth-header.interceptor.ts` already reads for every real HTTP request this app makes
(see "What 'authenticated' means here" below) — and returns one of two things: `true`,
letting navigation proceed, or a `UrlTree` (via `Router.createUrlTree(['/'])`),
redirecting.

It deliberately never calls `router.navigate()`. A guard that returns `false` cancels
the current navigation and stops — nothing else happens automatically, so a guard that
also calls `router.navigate()` on the side is *starting a second, independent
navigation* while the first one is still being cancelled. Those two navigations can race:
depending on timing, the user can briefly see a blank router-outlet (the first
navigation's cancellation) before the second one (the redirect) lands, or in slower cases
the two can interleave in ways that are hard to reproduce and harder to test
deterministically. Returning a `UrlTree` avoids the race entirely — the router treats the
`UrlTree` as the resolved *outcome* of the same navigation, atomically, so there is only
ever one navigation happening. It is also more directly testable: `auth.guard.spec.ts`
asserts on the guard's *return value* (`expect(result).toBeInstanceOf(UrlTree)` and its
`.toString()`), not on a side effect recorded by a router spy, which is a stronger and
less brittle assertion — a spy can be satisfied by a guard that happens to call
`navigate()` correctly today and races tomorrow after an unrelated refactor; asserting
the return type structurally cannot.

## The three route-param edges, and how each is handled

The real id field is `Quote.Id` (`day-3/task-3/QuotesApi/Quotes/Quote.cs:5`), a C# `int`,
always non-negative (`InMemoryQuoteRepository.cs`'s `_nextId` only increments). Angular
route params always arrive as strings, so `quote-detail-page.ts` validates against
`/^\d+$/` before ever treating the param as a number:

| Edge | Real trigger in this app | Outcome |
|---|---|---|
| **Missing** | `/quotes` (no id segment) — a real second route entry in `app.routes.ts` pointing at the same lazy component, so this is exercised by real navigation, not only a unit test | `paramProblem` computed to `'missing'`; renders `[data-testid="detail-page-status-missing"]`; **no HTTP call is made** (confirmed by `httpMock.expectNone('/api/quotes')` in the test) |
| **Malformed** | `/quotes/abc` | fails `/^\d+$/`; renders `[data-testid="detail-page-status-malformed"]` with the actual bad value echoed back; no HTTP call made |
| **Well-formed, not found** | `/quotes/9999` | passes the regex, so `getQuoteDetail(9999)` really is called against `GET /api/quotes`; the client-side `.find()` (see Contract discovery below) returns nothing; renders `[data-testid="detail-page-status-not-found"]`, distinct from the malformed branch |

All three are asserted separately in `quote-detail-page.spec.ts`, each checking the
specific `data-testid` for its branch and that the wrong branches are absent — collapsing
them into one generic "error" state would have hidden exactly the distinction the task
asks for.

## View Transitions and the browser-support caveat

`provideRouter(routes, withComponentInputBinding(), withViewTransitions())` in
`app.config.ts` wraps every router navigation in `document.startViewTransition()` when
the browser implements the View Transitions API, and navigates synchronously without it
when the browser does not — there is no separate fallback code path to write or
maintain; Angular's `withViewTransitions()` already does this internally. The list items
in `quote-browser.html` and the detail page's root element in `quote-detail-page.html`
both carry a matching `[style.view-transition-name]="'quote-detail-' + id"`, so the
browser *can* attempt to morph the clicked list item into the detail panel when it
navigates.

Two honest caveats, not glossed over:

1. **Browser support.** As of this writing, the View Transitions API ships in
   Chromium-based browsers (Chrome/Edge 111+) but not in Safari. Devansh uses Safari, so
   he should expect navigation to work completely normally — the detail page renders
   correctly, the guard and route params behave identically — but **no animation at
   all**, because `document.startViewTransition` is simply undefined there and
   `withViewTransitions()` silently skips straight to a normal navigation. That is a
   browser-support fact, not a bug to chase.
2. **This app's composition means the "old" element never disappears.** `QuoteBrowser`
   (the list) stays mounted the entire time — it is not itself routed, only the detail
   page is (see PROVENANCE.md #7 for why). That means when a view transition *is*
   attempted, the list item carrying a given `view-transition-name` is often still
   present in the DOM at the same moment the detail page (carrying the *same* name)
   appears, which the View Transitions spec treats as a duplicate name in one snapshot.
   Per spec this degrades gracefully — the browser drops the named match for that pair
   and falls back to a plain cross-fade for the affected region, it does not throw or
   block navigation — but it does mean a true shared-element "morph" is unlikely to be
   visible even in a browser that supports the API, given this particular UI shape. The
   wiring is correct and does what the brief asked; a picture-perfect morph would need a
   UI where the list item is actually removed when its detail is shown, which was out of
   scope here (see PROVENANCE.md #7 — the existing `QuoteBrowser` composition was
   deliberately left untouched).

Fallback correctness (not the animation itself, which cannot be observed outside a real
browser) is verified by test: `quote-detail-page.spec.ts`'s "VIEW TRANSITION FALLBACK"
test asserts `typeof document.startViewTransition === 'undefined'` first — true because
jsdom, the real DOM environment every spec in this project runs under, does not
implement the API at all — and then performs a real navigation and confirms it completes
and renders correctly. This means every single test in this file already exercises the
no-support fallback path for real, not as a special case; that one test just makes the
fact explicit.

## What "authenticated" means here, and what this deliberately does not include

There is no real authentication system in this app, before or after this task. Day 15
built a local-dev convenience: `dev-login.ts` calls the real
`POST /api/auth/login` and stores whatever `access_token` comes back under the
`devAuthToken` localStorage key; `auth-header.interceptor.ts` reads that same key (via
`AuthTokenService`) to attach `Authorization: Bearer <token>` to same-origin `/api/`
requests. `auth.guard.ts` reuses that exact same `AuthTokenService` — not a second,
competing notion of "signed in" — and treats "authenticated" as nothing more than "a
non-empty string is present under that key". It does not check the token is a real,
unexpired, correctly-signed JWT; it does not know anything about scopes or claims; it
would treat any non-empty string, real token or not, as "authenticated". This is
deliberate and matches Day 15's own existing convenience — this is a routing exercise, a
mentor reading this should see a guard that proves the *routing and redirect mechanics*
work correctly, not a claim that real authentication was implemented. No token is ever
hardcoded anywhere in this codebase; every guard/interceptor test uses an obviously fake
literal (`'fake-token-for-tests'`), and `verify-structural.mjs`'s JWT-shape check
(`output/structural-check-final.txt`) confirms none of the real, empty-body 401 shapes
captured from the live API or committed test fixtures contain anything JWT-shaped.

## The eight resolved interpretations, in full

1. **The contract.** Re-verified directly against `day-3/task-3/QuotesApi/Program.cs`
   and `Quotes/Quote.cs` rather than trusted from day-15's own comments (which agreed).
   List: `GET /api/quotes` (Program.cs:361-362, no auth). No per-item GET/detail
   endpoint exists at all — only auth-gated `PUT`/`DELETE /api/quotes/{id:int}` mutations
   use the id param. Id field: `Quote.Id`, `int`, non-negative. Because no detail
   endpoint exists, the detail route resolves by re-fetching the list and finding the
   requested id client-side — exactly what `quote-api.ts`'s existing `getQuoteDetail()`
   already did before this task, so `quote-detail-page.ts` follows the same pattern
   rather than inventing a new one.
2. **What "authenticated" means here.** See the dedicated section above. Only "a
   `devAuthToken` is present"; no real auth system built or implied.
3. **UrlTree, not navigate.** See the dedicated section above. Guard tests assert the
   returned `UrlTree` itself, not a router-spy side effect.
4. **The three param edges are distinct.** Missing (`/quotes`, no HTTP call), malformed
   (`/quotes/abc`, fails `/^\d+$/`, no HTTP call), well-formed-but-not-found
   (`/quotes/9999`, real HTTP call, real client-side lookup miss) — each has its own
   `data-testid`, its own rendered message, and its own test.
5. **View Transitions and browser support.** Wired via `withViewTransitions()`; no
   animation expected in Safari (Devansh's browser); fallback verified by test under
   jsdom's real (not simulated) absence of the API; a caveat about this app's
   list-stays-mounted composition limiting how well the morph can actually render is
   stated plainly above rather than glossed over.
6. **Testing navigation without a browser.** `RouterTestingHarness` and
   `HttpTestingController` — no network, no running API. Lazy loading is proven only by
   the real production build and grep in `output/lazy-load-proof.md`, never claimed by a
   test.
7. **Regression risk from introducing routing.** The app had no router before this task
   (component composition only — see the Tooling Gate section below). Nine carried files
   needed a small, justified change each so routing could coexist with them; every one is
   listed with its reason in `PROVENANCE.md`, and every existing assertion in every one of
   those files is unchanged in substance — only providers arrays and one wrapping
   `<section>`/one sibling `<a>` were added.
8. **Evidence.** Every version string, route, field name, chunk name, size, test count,
   and grep result quoted in this README, `PROVENANCE.md`, `submission.md` and
   `verification-log.md` comes from a real command captured to `output/`.

## Tooling gate

The carried app (`day-15/task-1`, and therefore `day-16/task-1` before this task's
changes) had **no router configured at all** — `app.config.ts` had no `provideRouter`
call, and `app.ts`/`app.html` rendered `QuoteBrowser` and both forms directly by
component composition, gated behind a `DevLogin` boolean signal. Routing is introduced
for the first time in this task, which is why interpretation 7 above applies as broadly
as it does.

Angular CLI, Node, npm, and the carried `package.json`'s Angular version are all reported
verbatim in `submission.md` and were not changed by this task — no dependency was
upgraded, and the Angular major stayed at 21.

## How to run and test the app

```
cd day-16/task-1
npm install                          # already done; re-run is idempotent
npm test                             # ng test — Vitest, runs once (not watch)
node scripts/verify-structural.mjs   # structural checks (no NgModule, no `any`, no eager
                                      # import of the detail component, guard wiring, etc.)
npm start                            # ng serve, for manual/browser verification
npx ng build --configuration production   # the build the lazy-loading proof depends on
```

No SQL Server, Docker container, or running QuotesApi instance is required for any of the
above — every HTTP interaction in every test is intercepted by
`HttpTestingController`. See `verification-log.md` for the manual `ng serve` /
network-tab script Devansh can use to see the lazy chunk load for himself, since that
part genuinely cannot be done from here.
