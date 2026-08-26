# Day 15 Task 1 — HttpClient + interceptors

A copy-forward of `day-14/task-2` (see PROVENANCE.md) with a characterization test
pinning the real QuotesApi contract, plus three functional HTTP interceptors wired
against that pinned contract: auth header, retry-with-backoff, and error mapping.

## What a characterization test pins, and why it comes first

A characterization test pins EXISTING behaviour — what the system actually does right
now — not a specification of what it should do. It is the opposite of a spec test: a
spec test fails when the code doesn't do what it's supposed to; a characterization test
fails when the code stops doing what it used to do, even if "what it used to do" was
never written down anywhere else. The point here is narrower and more concrete: if
someone later renames a field on `Quote`, adds a pagination parameter to
`GET /api/quotes`, or changes what a 4xx response looks like, `quotes-api-contract.spec.ts`
should be the thing that breaks — not the Angular app silently mis-rendering, and not a
mentor discovering it by hand. It has to exist and pass BEFORE any interceptor code, so
that everything built after it is being wired against a contract that was independently
verified, not assumed. `output/characterization-test-green-pre-interceptor.txt` and
verification-log.md are the evidence this ordering actually happened, not just a claim.

## Why only idempotent GETs are retried, and why 4xx is never retried

A GET is idempotent: making the same request twice has the same effect as making it
once, so replaying it after a failure is safe. A POST, PUT, or PATCH is not — replaying
a create after a timeout can create the row twice, and there is no way for the client to
tell "the write never happened" apart from "the write happened but the response was
lost." So `retryTransientGetInterceptor` structurally refuses to touch anything but GET
(`if (req.method !== 'GET') return next(req);`), not just by convention.

Separately, a 4xx will not change on repeat — a 400 stays a 400, a 404 stays a 404 —
so retrying one wastes a round trip and delays the real error reaching the caller for no
benefit. Only a network error (status 0) or a 5xx is treated as transient and eligible
for retry; everything else re-throws immediately from inside the `delay` callback.

Cap and backoff: `RETRY_MAX_ATTEMPTS = 3` (one initial attempt plus up to two retries),
base delay `RETRY_BASE_DELAY_MS = 200`, backoff factor `RETRY_BACKOFF_FACTOR = 2`, so the
two possible retry delays are 200ms then 400ms. Tests use vitest's fake timers
(`vi.useFakeTimers()` / `vi.advanceTimersByTimeAsync()`) so these delays are virtual —
the suite never actually waits.

## ProblemDetails and ValidationProblemDetails — and what the real API actually returns

ProblemDetails (RFC 7807) is a standard JSON error shape ASP.NET Core can produce —
`{ type, title, status, detail, instance }`. ValidationProblemDetails extends it with an
`errors` object mapping field names to arrays of validation messages, and ASP.NET Core's
`[ApiController]` attribute produces one automatically on model-binding/validation
failure.

The real `day-3/task-3/QuotesApi` does neither. It has no `[ApiController]` anywhere (it
is a Minimal API, built entirely from `app.MapGet`/`MapPost`/etc. in `Program.cs`) and
never calls `AddProblemDetails()` — confirmed by `grep -rn
"ProblemDetails|ApiController|AddProblemDetails|ValidationProblem"` across its entire
source tree, which returns zero matches. Every 4xx observed live from a real running
instance (`output/headers-401-post-unauth.txt`, `output/headers-405-quotes-id.txt`) has
an **empty body** — `Content-Length: 0`. `mapHttpErrorToAppError()` in
`app-http-error.ts` still handles a real ProblemDetails/ValidationProblemDetails body
(the brief asks for it, and some other API might return one), but the path that matters
most for THIS API is the fallback: a status-code-keyed generic friendly message for a
4xx/5xx with no parseable ProblemDetails shape at all, so a plain-text or empty-body
error still produces a sane typed error instead of throwing a raw `HttpErrorResponse` at
whatever called `QuoteApi`.

## Why interceptor order matters here

Functional interceptors run request-side in registration order and response-side in the
reverse order — the first interceptor in the array is outermost (its `next(req)` call is
"give me the response after everything after me has run"), the last is innermost,
closest to the actual network call. This app registers, via
`src/app/http/api-interceptors.ts` (imported by `app.config.ts`):

```
[authHeaderInterceptor, errorMappingInterceptor, retryTransientGetInterceptor]
```

- `authHeaderInterceptor` must be outermost (first) so the header it sets is baked into
  the request object once; `retryTransientGetInterceptor`'s own internal retries reuse
  that same header-bearing request for every attempt without authHeaderInterceptor
  needing to run again.
- `errorMappingInterceptor` must come before `retryTransientGetInterceptor` (be more
  "outer") so it only ever observes retry's FINAL settled outcome — a success, or the
  error left after the cap is exhausted — never an intermediate failure a later retry
  attempt went on to fix. Concretely: retry's own `next` call points at the backend
  directly (it's innermost), so its internal retries never re-enter errorMapping; the
  Observable errorMapping's `catchError` sees is whatever retry's pipeline ultimately
  settles on.
- `retryTransientGetInterceptor` is innermost/last, closest to the network, so its own
  retries stay invisible to both interceptors above it.

`interceptor-order.spec.ts` proves this with two tests run against the exact same
`API_INTERCEPTORS` array `app.config.ts` registers (not a hand-copied duplicate): a
request that fails once then succeeds surfaces no error at all to the caller, and every
retried attempt still carries the auth header; a request that exhausts every retry
surfaces exactly one mapped `AppHttpError`, never a raw intermediate failure. The Phase 5
mutation check (Mutation B, see verification-log.md) proves the reverse — swapping retry
and errorMapping breaks retry entirely, because retry's `isTransientFailure` check
requires a raw `HttpErrorResponse` and would instead only ever see an already-mapped
`AppHttpError`.

`devTokenInterceptor` (carried, untouched — see PROVENANCE.md) stays registered first,
ahead of all three; it is a no-op unless a token was set manually via the browser
console, so its position relative to the three above has no observable effect in any
test or in normal use.

## Token safety

`AuthTokenService.getToken()` reads `localStorage['devAuthToken']` — the same key
`dev-login.ts`/`dev-token.interceptor.ts` already use — so there is exactly one real
source of truth for "does this app have a token," never a hardcoded string. Tests use
the obviously-fake literal `'test-token'`. `authHeaderInterceptor` only attaches the
header to a request whose resolved URL is same-origin AND under `/api/` — a request to a
different origin (or a same-origin request outside `/api/`, e.g. `/assets/...`) never
gets the header, proven by dedicated tests. `scripts/verify-structural.mjs` scans every
source file AND every captured `output/` file for a JWT-shaped string
(`eyJ...\...\...`) and fails if one is found; none is.

## The nine resolved interpretations, in full

1. **The contract.** Pinned from `day-3/task-3/QuotesApi/Program.cs` (route,
   pagination — there is none) and `Quotes/Quote.cs` (fields). The real route is
   `GET /api/quotes` with NO query parameters read anywhere — the Academy's example
   (`?page=N&size=N`) does not apply; a request carrying those params is silently
   ignored (confirmed live: identical body with or without them). The real fields are
   `{id, ownerId, text, author?}` — `ownerId` is real and required; the Academy's
   example `{id, author, text}` omits it. The real error shape, on every 4xx reachable
   without a token, is an empty body — never ProblemDetails/ValidationProblemDetails
   (zero matches for either across the whole QuotesApi source tree). All three
   divergences are stated in submission.md, not hidden.
2. **What "characterization test" means here.** See above — pins existing behaviour,
   not a spec of desired behaviour, and is required to be green before any interceptor
   exists. `quotes-api-contract.spec.ts` fulfils this; see verification-log.md for the
   real timestamped proof of the ordering.
3. **Idempotency.** Retry limited structurally to GET; 4xx never retried since it won't
   change on repeat. See "Why only idempotent GETs are retried" above.
4. **Fake timers.** `vi.useFakeTimers()` / `vi.advanceTimersByTimeAsync()` — vitest is
   this app's test runner (`@angular/build:unit-test`, confirmed via `angular.json`),
   and `vitest/globals` is already in `tsconfig.spec.json`'s `types`, so `vi` is
   available with no new import and no new dependency.
5. **Interceptor order.** `[authHeaderInterceptor, errorMappingInterceptor,
   retryTransientGetInterceptor]`, justified and tested — see above.
6. **Token safety.** See "Token safety" above.
7. **Regression risk on carried tests.** Zero carried spec files needed to change.
   `quote-browser.ts` did change (to surface `AppHttpError.friendlyMessage`), but every
   carried test that exercises it configures its own bare `provideHttpClient()` with no
   interceptors — exactly like `quote-api.spec.ts`, `create-quote-form.spec.ts`, etc.
   already did before this task — so in that test environment the error handlers'
   `instanceof AppHttpError` check is always false and the code takes the exact original
   fallback path. Confirmed by running the carried suite unmodified: 56/56, same as the
   in-place baseline.
8. **No PATCH, no custom serialization.** The second "What this builds" tag names them;
   neither the body paragraphs nor the exercise text ask for either. Not built.
9. **Angular version unchanged.** `package.json`/`package-lock.json` carried verbatim;
   `@angular/core` stays `^21.2.0`. `npx ng version` reports Angular CLI 21.2.21.

## How to run and test the app

```bash
npm install
npm test              # ng test -- vitest, all suites
node scripts/verify-structural.mjs   # structural checks (no NgModule, no `any`, etc.)
npm start              # ng serve, dev server
npm run build           # ng build
```

### Testing a real save locally

Same convenience the carried app already documents: `dotnet run` from
`day-3/task-3/QuotesApi`, then either sign in via the `DevLogin` component in the app
(POSTs to the real `/api/auth/login`) or set `localStorage.devAuthToken` manually in the
browser console. No token is ever hardcoded or committed anywhere in this repository.
