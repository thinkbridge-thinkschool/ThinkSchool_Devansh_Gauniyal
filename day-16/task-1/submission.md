# Day 16 Task 1 — Routing, lazy loading, guards

This replicates day-15/task-1 at commit `f1c2121b43ea269079fcc5030b4f3bc0bbcee029` and
adds routing, lazy loading, a guard and a view transition; earlier folders are unchanged.

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-16/task-1/day-16/task-1

## Notes for mentor

### 1. The brief

Take the Angular 21 app from day-15/task-1 and copy it into day-16/task-1 untouched. Do not modify day-15 or anything earlier.

Working inside day-16/task-1 only, add routing against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi. Read its controller and DTOs first and use the ACTUAL list route, the ACTUAL detail route, and the ACTUAL identifier field name and type. Do not invent an endpoint, a field name or a param type.

Build:
- a route table with the quotes list as a route, and a quote detail as a LAZY-LOADED route using loadComponent — the detail component must not appear in the main bundle
- a route param carrying the real quote id, typed to match the API's real id type, read with the modern input-binding or ActivatedRoute approach the app already uses
- a functional auth guard, a CanActivateFn, protecting the detail route: it returns true when authenticated and returns a UrlTree redirect when not. Return a UrlTree rather than calling router.navigate, so the navigation is cancelled cleanly instead of racing a second one.
- handling for a missing or invalid route param — a param that is absent, malformed for the id's real type, or refers to a quote that does not exist. Each must produce a sensible outcome, not a crash or a blank screen.
- a View Transition between the list and the detail using the router's withViewTransitions, with matching view-transition-name styling on the elements that should morph. If the browser does not support View Transitions, navigation must still work normally — verify the fallback rather than assuming it.

Everything already in the app — the list, the detail, the stale-response guard, both forms, the interceptors, the characterization test — must keep working. Do not degrade any existing test. Change nothing else: no refactors, no renames, no dependency upgrades, no restyling beyond the view-transition-name styles the transition requires.

### 2. The agent's output

**Route config — `src/app/app.routes.ts`**
```ts
/**
 * Route table for the quote detail page. The real QuotesApi (day-3/task-3/QuotesApi,
 * see Program.cs:361-362) has no per-item GET route -- only GET /api/quotes (list, no
 * auth) and PUT/DELETE /api/quotes/{id:int} (auth-gated mutations). There is therefore
 * no real "detail endpoint" to route a param onto the API with; quote-api.ts's
 * getQuoteDetail() already works around this by re-calling the one real read endpoint
 * and resolving the id client-side, and quote-detail-page.ts does the same. The route
 * param below still exercises real router navigation and a real (client-side) lookup by
 * the real id field -- Quote.Id, a non-negative int (Quote.cs) -- it is just not backed
 * by a server-side detail route, because none exists.
 *
 * Both routes point at the SAME lazily-loaded component (quote-detail-page.ts):
 * 'quotes/:id' is the normal detail route; the paramless 'quotes' route exists so the
 * "missing route param" edge is exercised by a real navigation, not only a unit test
 * with the input left unset.
 */
import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: 'quotes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes/quote-detail-page/quote-detail-page').then((m) => m.QuoteDetailPage),
  },
  {
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes/quote-detail-page/quote-detail-page').then((m) => m.QuoteDetailPage),
  },
];
```

**Guard — `src/app/auth/auth.guard.ts`**
```ts
/**
 * Functional route guard (CanActivateFn) for the lazy-loaded quote detail routes (see
 * ../app.routes.ts). Checks the SAME token source auth-header.interceptor.ts already
 * reads -- AuthTokenService, backed by the devAuthToken localStorage key dev-login.ts
 * writes -- rather than a second, competing notion of "signed in". No token is ever
 * hardcoded here.
 *
 * Returns `true` when a token is present, so navigation proceeds. Returns a UrlTree
 * (redirecting to '/') when it is not, so the navigation is cancelled and replaced
 * atomically. This deliberately does NOT call router.navigate() -- a guard that returns
 * false and separately calls navigate() lets the in-flight (blocked) navigation and the
 * new (redirect) navigation race each other; returning a UrlTree lets the router itself
 * treat the redirect as the resolved outcome of the same navigation, with no race and no
 * blank intermediate screen.
 *
 * "Authenticated" here means only "a devAuthToken is present in localStorage" -- the
 * same local-dev convenience Day 15 already built, not a real authentication system. See
 * README.md, "What 'authenticated' means here", for the full interpretation and its
 * limits.
 */
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthTokenService } from '../http/auth-token.service';

export const authGuard: CanActivateFn = (): true | UrlTree => {
  const hasToken = !!inject(AuthTokenService).getToken();
  if (hasToken) {
    return true;
  }
  return inject(Router).createUrlTree(['/']);
};
```

**Detail route component — `src/app/quotes/quote-detail-page/quote-detail-page.ts`**
```ts
/**
 * Route-driven detail page for the lazy-loaded 'quotes' / 'quotes/:id' routes -- see
 * ../../app.routes.ts. This is deliberately separate from QuoteBrowser's existing
 * inline detail pane (quote-browser.ts), which keeps working completely unchanged.
 * This component exists only to be reached by router navigation, so it can prove the
 * lazy chunk, the guard, the route-param edges and the view transition this task asks
 * for, without touching the carried composition-based feature at all.
 *
 * The real API (day-3/task-3/QuotesApi, see quote-api.ts's own header comment) has no
 * per-item GET route -- only GET /api/quotes (list). getQuoteDetail() below re-calls
 * that one real endpoint and resolves the requested id client-side, exactly as
 * QuoteBrowser already does; the "detail" fetch is still a genuine, independently-timed
 * HTTP round trip.
 */
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppHttpError } from '../../http/app-http-error';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

export type QuoteDetailPageParamProblem = 'missing' | 'malformed';

// The real id field is Quote.Id, a non-negative auto-incrementing C# `int`
// (day-3/task-3/QuotesApi/Quotes/Quote.cs; InMemoryQuoteRepository.cs seeds
// `_nextId` and never issues a negative or non-integer id) -- so a route param is
// "malformed" here whenever it is not a plain non-negative integer string.
const NON_NEGATIVE_INTEGER = /^\d+$/;

@Component({
  selector: 'app-quote-detail-page',
  imports: [RouterLink],
  templateUrl: './quote-detail-page.html',
  styleUrl: './quote-detail-page.css',
})
export class QuoteDetailPage {
  private readonly api = inject(QuoteApi);

  // Route param bound by withComponentInputBinding() (see app.config.ts): undefined on
  // the paramless 'quotes' route, a raw (possibly non-numeric) string on 'quotes/:id'.
  readonly id = input<string>();

  protected readonly paramProblem = computed<QuoteDetailPageParamProblem | null>(() => {
    const raw = this.id();
    if (raw === undefined || raw.trim() === '') {
      return 'missing';
    }
    return NON_NEGATIVE_INTEGER.test(raw) ? null : 'malformed';
  });

  private readonly numericId = computed<number | null>(() => {
    const raw = this.id();
    return raw !== undefined && this.paramProblem() === null ? Number(raw) : null;
  });

  protected readonly loading = signal(false);
  protected readonly quote = signal<Quote | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  // Stale-response guard, the same pattern QuoteBrowser.selectQuote() already uses: `id`
  // is captured at request time by this closure, and a later id change may move
  // numericId() on before this response lands, in which case it must be discarded.
  private readonly loadOnIdChange = effect(() => {
    const id = this.numericId();
    this.quote.set(null);
    this.errorMessage.set(null);
    if (id === null) {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.api.getQuoteDetail(id).subscribe({
      next: (found) => {
        if (this.numericId() !== id) {
          return;
        }
        this.quote.set(found ?? null);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        if (this.numericId() !== id) {
          return;
        }
        this.errorMessage.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quote detail.');
        this.loading.set(false);
      },
    });
  });
}
```

### 3. The verification log

**Real contract, cited from source.** List: `GET /api/quotes`
(`day-3/task-3/QuotesApi/Program.cs:361-362`, no auth). No per-item detail endpoint
exists at all — only auth-gated `PUT`/`DELETE /api/quotes/{id:int}` mutations use the id
param (Program.cs:378-413). Id field: `Quote.Id`, `int`, non-negative
(`day-3/task-3/QuotesApi/Quotes/Quote.cs:5`, `InMemoryQuoteRepository.cs`'s `_nextId`).
Because no detail endpoint exists, both new routes and `quote-detail-page.ts` resolve the
id client-side against the one real list endpoint, exactly as `quote-api.ts`'s existing
`getQuoteDetail()` already did.

**Guard pass vs redirect, real test names and returned UrlTree.**
`auth.guard.spec.ts`: `AUTHENTICATED: returns true when a devAuthToken is present, so
navigation proceeds` — passes, guard returns `true`. `UNAUTHENTICATED: returns a UrlTree
redirecting to "/" (not a boolean) when no token is present` — passes, asserts
`result instanceof UrlTree` and `result.toString() === '/'`. `ROUTE CONFIG: authGuard is
actually attached to both quote-detail routes, not just defined in isolation` — passes,
asserts against `routes`, not the guard alone. `quote-detail-page.spec.ts`'s `GUARD PASS`
and `GUARD REDIRECT` tests exercise the same two states through a real
`RouterTestingHarness` navigation, not just the guard function in isolation.

**Lazy chunk, real chunk name, grep, before/after sizes.** Production build
(`output/build-after-routing.txt`) shows a separate named lazy chunk,
`chunk-5PWGBFH4.js` (name `quote-detail-page`, 3.56 kB raw / 1.22 kB transfer), distinct
from the two initial chunks. `grep -o "Quote detail" dist/app/browser/main-WRLQ7LK7.js
dist/app/browser/chunk-VE5SX3A7.js` — no output, exit 1: the detail component's
distinctive template text is absent from both eager files. The same string greps found
in `dist/app/browser/chunk-5PWGBFH4.js`. Main bundle size before routing was added:
261.52 kB raw / 69.33 kB transfer (`output/build-before-routing.txt`); after: 352.61 kB
raw / 92.11 kB transfer initial, with the detail page itself only 3.56 kB/1.22 kB of
that as a separate lazy chunk — the ~91 kB initial growth is the Router itself, used
app-wide, not the detail page. Full proof, including the one expected loader-glue string
match and why it does not contradict the above, is in `output/lazy-load-proof.md`.

**The three route-param edges.** `PARAM MISSING` (`/quotes`, real navigation, no HTTP
call) → `detail-page-status-missing`. `PARAM MALFORMED` (`/quotes/abc`, real navigation,
no HTTP call) → `detail-page-status-malformed`. `PARAM WELL-FORMED BUT NOT FOUND`
(`/quotes/9999`, real HTTP call flushed with a list that omits 9999) →
`detail-page-status-not-found`, asserted distinct from the malformed state. All three in
`quote-detail-page.spec.ts`.

**Carried tests.** All 79 carried tests (12 files) still pass, unchanged in substance.
Nine carried files needed a small, justified addition each so routing could coexist with
previously-non-routed code (mostly `provideRouter(...)` added to a `TestBed` providers
array, since `RouterLink`/`RouterOutlet` inject `Router` at construction) — full list and
justification for each in `PROVENANCE.md`. Day 13's stale-response guard
(`QuoteBrowser.selectQuote`'s race test), both create-a-quote forms, the Day 15
interceptor specs, and the characterization test (`quotes-api-contract.spec.ts`) all
still pass, confirmed individually in the 89-test run
(`output/final-test-run.txt`).

**The one genuine bug caught.** `quote-detail-page.spec.ts`'s `TestBed` was configured
with `provideRouter(routes)`, omitting `withComponentInputBinding()` — the extension
`app.config.ts` actually registers alongside it. Real result: `QuoteDetailPage.id()`
(bound to the real `:id` param, itself bound to `Quote.Id` from
`day-3/task-3/QuotesApi/Quotes/Quote.cs`) stayed `undefined` regardless of the navigated
URL, so every test silently collapsed onto the "missing id" branch. Surfaced as four real
test failures, most tellingly `Error: Expected one matching request for criteria "Match
URL: /api/quotes", found none.` on a test that navigated to `/quotes/1` and should have
triggered a real HTTP call. Fixed by adding `withComponentInputBinding()` to the test's
`provideRouter()` call to match the real app registration; all 15 files / 89 tests then
passed. Full write-up in `verification-log.md`.

**What breaks if this changes.** If `Quote.Id` became a GUID instead of an `int`,
`quote-detail-page.ts`'s `/^\d+$/` malformed-id check would reject every real id as
malformed — a runtime behavior change, not a compile error, since route params are
always strings regardless of the real field's type. If the API grew a real per-item GET
route, `quote-api.ts`'s and `quote-detail-page.ts`'s client-side `.find()` workaround
would keep working but would be redundant — a real request-per-id would be strictly
better and nothing here would force that migration automatically.

**Network-tab confirmation.** PENDING — Devansh's own to perform; script and current
status in `verification-log.md`.

### Interpretations

- Replicated from day-15/task-1 at commit `f1c2121b43ea269079fcc5030b4f3bc0bbcee029`; no
  detail endpoint exists on the real API (`day-3/task-3/QuotesApi/Program.cs`), so the
  detail route resolves client-side against the one real list endpoint, same as the
  carried `quote-api.ts` already did.
- Real list route `GET /api/quotes` (Program.cs:361-362); real id field `Quote.Id`, `int`
  (`Quotes/Quote.cs:5`); both file paths cited above and in PROVENANCE.md.
- "Authenticated" means only "a `devAuthToken` is present in localStorage" — the Day 15
  dev-login convenience, reused via `AuthTokenService`. No real auth system was built.
- Guard returns a `UrlTree`, not `router.navigate()` — avoids racing a second navigation
  against the cancelled first one; tested by asserting the returned value itself.
- Three param edges — missing, malformed, well-formed-but-not-found — each has a distinct
  `data-testid`, message, and test; never collapsed into one generic error.
- View Transitions wired via `withViewTransitions()`; no animation expected in Safari;
  fallback verified by test under jsdom's real absence of the API, not a simulation.
- Lazy loading proven by the real production build and a grep for a distinctive string,
  never claimed by a test — tests cannot prove a bundling outcome.
- Angular major unchanged at 21; no dependency upgraded.

## What did you learn this session?

A lazy route can quietly end up in the main bundle anyway if something else in the app imports that component directly — there's no error or warning either way, so I can't just trust the route file. I have to actually build and grep the output to know for sure.

## What would break this?

If I (or anyone) later added an unrelated import of the detail component somewhere else in the app, it would silently get bundled eagerly again with nothing flagging it. And if the API's quote id ever changed from an int to a GUID, my malformed-id check would start rejecting every real id, since it only accepts digits.
