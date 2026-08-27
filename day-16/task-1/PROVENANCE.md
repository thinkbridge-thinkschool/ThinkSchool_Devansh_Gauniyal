# Provenance — day-16/task-1

**Source:** `day-15/task-1` at commit `f1c2121b43ea269079fcc5030b4f3bc0bbcee029` (the
commit that last touched that folder, and `HEAD` of `day-15/task-1` at the time this
branch was cut). Verified as the latest complete app in Phase 1 — it carries the Day 13
list-plus-detail (via `QuoteBrowser`), both create-a-quote forms (reactive and Signal
Forms), the Day 15 characterization test (`quotes-api-contract.spec.ts`) and the HttpClient
interceptor chain (`src/app/http/*`, `src/app/auth/dev-token.interceptor.ts`).

**Copy method:** `rsync -av --exclude='node_modules' --exclude='dist'
--exclude='.angular' --exclude='coverage' --exclude='.git'
day-15/task-1/ day-16/task-1/`, run once, followed by `npm install` inside
`day-16/task-1`. See verification-log.md, mistake #1, for the correction this needed
(the first run also needed `output/` and day-15's own task-narrative docs removed by
hand afterward, since `--exclude='output'` had been left off the command and those docs
describe day-15's task, not day-16's).

## Files carried unchanged

Every file under `day-16/task-1/src/`, `public/`, `scripts/`, config files
(`angular.json`, `tsconfig*.json`, `.editorconfig`, `.prettierrc`, `.gitignore`,
`.vscode/`, `proxy.conf.json`), `package.json` and `package-lock.json` — **except** the
seven files listed below, which were modified, and the files newly added, listed
further below.

## Files carried and modified, each with its justification

All seven modifications exist because the app had no router before this task
(interpretation 7 in README.md) — introducing one is the task, and each change here is
the smallest edit that let a previously-non-routed file coexist with routing. No
modification changes any existing assertion's strength; every carried test still passes
at its original count (79/79).

1. **`src/app/app.config.ts`** — added `provideRouter(routes, withComponentInputBinding(),
   withViewTransitions())` to the providers array, and the corresponding imports.
   Justification: the brief requires a route table, component-input-bound route params,
   and a View Transition; `provideRouter` is the only place any of that can be wired in.

2. **`src/app/app.ts`** — added `RouterOutlet` to the `imports` array.
   Justification: required for `<router-outlet />` in app.html to compile.

3. **`src/app/app.html`** — added one `<section data-testid="detail-route-outlet">`
   wrapping a `<router-outlet />`, placed after the existing `app-quote-browser` section.
   Justification: something must host the routed detail page; nothing existing was
   removed, reordered, or restyled beyond this one addition.

4. **`src/app/app.spec.ts`** — added `provideRouter(routes)` to the TestBed providers.
   Justification (interpretation 7): `RouterOutlet` now appears in `App`'s template and
   injects `Router` at construction; without a router provider, `TestBed.createComponent(App)`
   would throw `NullInjectorError` for every one of this file's three existing tests, none
   of which navigate anywhere or assert anything about routing. All three assertions are
   unchanged in substance.

5. **`src/app/quotes/quote-browser/quote-browser.ts`** — added `RouterLink` to the
   `imports` array (and a header comment explaining why).
   Justification: needed by the new "Open detail page" link (see #7). Nothing about
   `QuoteBrowser`'s own signals, HTTP calls, or stale-response guard was touched.

6. **`src/app/quotes/quote-browser/quote-browser.spec.ts`** and
   **`src/app/quotes/quote-browser/quote-browser-friendly-error.spec.ts`** — added
   `provideRouter([])` to each file's TestBed providers.
   Justification (interpretation 7): same `RouterLink`-injects-`Router`-at-construction
   reason as `app.spec.ts`. Every existing assertion in both files is byte-for-byte
   unchanged; only the providers array grew by one entry each.

7. **`src/app/quotes/quote-browser/quote-browser.html`** and
   **`src/app/quotes/quote-browser/quote-browser.css`** — each list `<li>` gained a
   `[style.view-transition-name]="'quote-detail-' + quote.id"` binding and a new
   `<a routerLink="['/quotes', quote.id]">Open detail page →</a>` sibling to the existing
   select button; the `.css` file gained a small block styling that new link. Justification:
   the brief's View Transition requires a real navigation trigger from the list to the
   routed detail page (the existing click-to-select button only ever sets local signals —
   it never navigates). The existing button, its `(click)="selectQuote(quote.id)"`
   handler, its `data-testid`, and every other existing behavior in this template are
   untouched; the new link is strictly additive.

8. **`scripts/verify-structural.mjs`** — extended the existing constructor-injection
   check to also scope to `: CanActivateFn` files (previously only `@Component(` and
   `: HttpInterceptorFn`), and added two new checks: that `quote-detail-page.ts` is never
   statically imported outside its own directory, and that `app.routes.ts` uses
   `loadComponent` rather than `component` for the detail routes. Justification: Phase 5's
   structural-check requirements explicitly ask for guard coverage and a static
   eager-import check backing up the build-output grep in `output/lazy-load-proof.md`;
   this project's own established convention (see the script's own header comment) is
   that static source-text checks like these live here, not in a Vitest spec, because the
   Vitest builder compiles specs through the browser-targeted pipeline with no Node
   built-ins.

*(Eight items are listed above; #6 covers two files under one justification and #7 covers
two files under one justification, hence "seven files modified" in the summary line
above refers to the seven distinct justifications, covering nine files total.)*

## Files newly added

- `src/app/auth/auth.guard.ts` — the functional `CanActivateFn` guard.
- `src/app/auth/auth.guard.spec.ts` — guard unit tests (authenticated, unauthenticated,
  route-config attachment).
- `src/app/app.routes.ts` — the route table.
- `src/app/app.routes.spec.ts` — structural test that the detail routes use
  `loadComponent`, not `component`.
- `src/app/quotes/quote-detail-page/quote-detail-page.ts` — the lazily-loaded detail
  page component.
- `src/app/quotes/quote-detail-page/quote-detail-page.html`
- `src/app/quotes/quote-detail-page/quote-detail-page.css`
- `src/app/quotes/quote-detail-page/quote-detail-page.spec.ts` — `RouterTestingHarness`
  integration tests: guard pass/redirect through real navigation, all three route-param
  edges, and the View Transitions fallback.
- `brief.md`, `PROVENANCE.md` (this file), `README.md`, `submission.md`,
  `verification-log.md` — this task's own documents (day-15/task-1's originals of the
  same filenames were not carried forward — see the copy-method note above).
- `output/` — this task's own captured build and test evidence (day-15/task-1's
  `output/` was not carried forward; day-16/task-1 has its own).

## Contract discovery this design depends on

- List endpoint: `GET /api/quotes` — `day-3/task-3/QuotesApi/Program.cs:361-362`, no
  `RequireAuthorization` call, so no auth required.
- **No per-item GET/detail endpoint exists** on the real API. Only
  `PUT /api/quotes/{id:int}` (Program.cs:378-387) and `DELETE /api/quotes/{id:int}`
  (Program.cs:389+) use the id route param, and both are auth-gated mutations
  (`RequireAuthorization(AuthorizationPolicies.CanEditQuotes)` /
  `RequireAuthorization()`), not reads. This matches what day-15/task-1's own
  `quote-api.ts` header comment already documented, re-verified directly against
  Program.cs rather than trusted at face value; the two agree.
- Id field: `Quote.Id`, a C# `int` — `day-3/task-3/QuotesApi/Quotes/Quote.cs:5`,
  `public sealed record Quote(int Id, string OwnerId, string Text, string? Author =
  null);`. `InMemoryQuoteRepository.cs`'s `_nextId` (seeded at 3, incremented per
  `Create()` call) confirms ids are always non-negative integers, never negative or
  fractional — the basis for `quote-detail-page.ts`'s `/^\d+$/` malformed-id check.
- Because there is no real detail endpoint, `app.routes.ts`'s two routes and
  `quote-detail-page.ts` work around this exactly as `quote-api.ts`'s existing
  `getQuoteDetail()` already does: re-call the one real read endpoint,
  `GET /api/quotes`, and resolve the requested id client-side. The route param still
  drives a real, independently-timed HTTP round trip and real router navigation; it is
  just not backed by a server-side detail route, because none exists to back it with.
