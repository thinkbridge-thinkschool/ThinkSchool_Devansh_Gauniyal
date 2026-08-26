# Day 15 Task 1 — HttpClient + interceptors

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-15/task-1/day-15/task-1

## Notes for mentor

This replicates `day-14/task-2` at commit `63878b077e8253d78ff997b08a14537badf4175d`
and adds the characterization test and interceptors; earlier folders are unchanged.

### 1. The brief

Take the Angular 21 app from day-14/task-2 and copy it into day-15/task-1 untouched. Do not modify day-14 or anything earlier.

Working inside day-15/task-1 only, do these in this order.

FIRST, before touching any UI or any interceptor: write a characterization test that pins my real Week-1 API contract. The API is the QuotesApi at day-3/task-3/QuotesApi. Read its controller and DTOs and pin what is actually there — the real list route, the real pagination parameters if it has any, the real response field names with exact casing, and the real error shape the API returns on a 4xx. If the endpoint is reachable without a token, call it for real and pin the observed response; if not, pin from the DTO source and say so. Run that test and show it green before writing anything else.

THEN wire HttpClient with functional interceptors against that pinned contract:
- an auth header interceptor that attaches the bearer token to requests going to my API and to nothing else; the token comes from a service or config, never a hardcoded string, and is never committed
- a retry interceptor that retries ONLY idempotent GET requests, ONLY on transient failures — network errors and 5xx — never on 4xx, never on POST, PUT or PATCH, with exponential backoff and a hard cap on attempts
- an error-mapping interceptor that turns a ProblemDetails or ValidationProblemDetails response into a typed application error carrying a friendly message, and for ValidationProblemDetails preserves the per-field errors so a form could show them

State and justify the interceptor order, and make sure the error mapping sees the final outcome rather than an intermediate retry.

Everything already in the app — the list, the detail, the stale-response guard, both forms — must keep working. Do not degrade any existing test. Change nothing else: no refactors, no renames, no dependency upgrades, no restyling.

### 2. The agent's output

**Characterization test — `src/app/quotes/quotes-api-contract.spec.ts`**

```typescript
/**
 * Characterization test for the real Week-1 API: day-3/task-3/QuotesApi.
 *
 * This pins EXISTING behaviour, not a specification of what the API should do. It must
 * stay green before any interceptor exists (Day 15 Task 1's required ordering) and it
 * must fail if the real route, its (non-existent) pagination parameters, the real
 * response field names, or the real error shape ever change.
 *
 * Every claim below cites the real source file it was read from. Two of the four facts
 * pinned here (the success body shape and the "4xx has no body" shape) were additionally
 * confirmed live against a running instance of the real API -- see
 * output/get-quotes-success-body.json, output/get-quotes-ignored-pagination-body.json,
 * output/headers-405-quotes-id.txt and output/headers-401-post-unauth.txt, captured
 * 2026-08-26 against http://127.0.0.1:5080. The Academy's own example
 * (`GET /api/quotes?page=N&size=N` -> `{id, author, text}`, errors as
 * ProblemDetails/ValidationProblemDetails) does NOT match this API on any of these
 * four points; each divergence is called out explicitly below and in submission.md.
 */
import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

// Read from day-3/task-3/QuotesApi/Program.cs:361-362 -- `app.MapGet("/api/quotes", ...)`.
// No route parameter, no query-string parameter is read anywhere in the handler body.
const REAL_LIST_ROUTE = '/api/quotes';

// Read from day-3/task-3/QuotesApi/Quotes/Quote.cs:5 --
//   public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null);
// ASP.NET Core's default Minimal API JSON options camel-case every property, and
// Program.cs never overrides that -- so the wire shape is exactly this, confirmed live.
const REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE = [
  { id: 1, ownerId: 'user-1', text: 'Security is a process.', author: null },
  { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.', author: null },
  { id: 3, ownerId: 'local-dev-caller', text: 'hello world', author: 'me' },
];

describe('QuotesApi real contract (characterization -- pins existing behaviour)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('PINS the real route: GET /api/quotes, with no id segment and no query string', () => {
    http.get(REAL_LIST_ROUTE).subscribe();

    const req = httpMock.expectOne(REAL_LIST_ROUTE);
    expect(req.request.method).toBe('GET');
    expect(req.request.url).toBe('/api/quotes');
    // The Academy's example is GET /api/quotes?page=N&size=N. The real handler
    // (Program.cs:361-362) takes no parameters at all -- there is no pagination to pin.
    expect(req.request.params.keys().length).toBe(0);
  });

  it('PINS that a request carrying page/size still hits the same route unchanged -- the API has no pagination to consume them', () => {
    // Not a recommendation to send these -- this documents what actually happens if
    // someone assumes the Academy's example and does send them: the real handler
    // ignores them entirely and returns the full, unpaginated list. Confirmed live:
    // output/get-quotes-ignored-pagination-body.json is byte-identical to
    // output/get-quotes-success-body.json.
    http.get(REAL_LIST_ROUTE, { params: { page: '1', size: '10' } }).subscribe();

    const req = httpMock.expectOne((r) => r.url === REAL_LIST_ROUTE);
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('10');
    req.flush(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);
  });

  it('PINS the real response field names and casing: id, ownerId, text, author -- not the Academy example {id, author, text}', () => {
    let received: unknown;
    http.get(REAL_LIST_ROUTE).subscribe((body) => (received = body));

    httpMock.expectOne(REAL_LIST_ROUTE).flush(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);

    expect(received).toEqual(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);
    const first = (received as Array<Record<string, unknown>>)[0];
    // ownerId is real and required; the Academy's example never mentions it at all.
    expect(first['ownerId']).toBe('user-1');
    expect(Object.keys(first).sort()).toEqual(['author', 'id', 'ownerId', 'text']);
  });

  it('PINS that a 4xx from the real API has an EMPTY body -- not ProblemDetails, not ValidationProblemDetails', () => {
    // Real capture, output/headers-401-post-unauth.txt: POST /api/quotes with no
    // Authorization header -> 401 Unauthorized, Content-Length: 0, WWW-Authenticate:
    // Bearer. grep -rn "ProblemDetails|ApiController|AddProblemDetails|ValidationProblem"
    // across the entire day-3/task-3/QuotesApi tree returns zero matches -- there is no
    // ApiController and no AddProblemDetails() call anywhere, so nothing in this API can
    // ever produce a ProblemDetails or ValidationProblemDetails body.
    let capturedStatus: number | undefined;
    let capturedBody: unknown;
    http.post(REAL_LIST_ROUTE, { text: 'x' }).subscribe({
      error: (err) => {
        capturedStatus = err.status;
        capturedBody = err.error;
      },
    });

    httpMock.expectOne(REAL_LIST_ROUTE).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(capturedStatus).toBe(401);
    expect(capturedBody).toBeNull();
  });
});
```

**Auth header interceptor — `src/app/http/auth-token.service.ts` and `src/app/http/auth-header.interceptor.ts`**

```typescript
import { Injectable } from '@angular/core';

// Same localStorage key dev-login.ts, dev-token.interceptor.ts and app.ts already read
// and write (see those files) -- one real source of truth for "is there a token", not a
// second competing one. No token value is ever hardcoded here; only this key NAME is a
// literal, and a key name is not a secret.
const DEV_AUTH_TOKEN_KEY = 'devAuthToken';

@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  getToken(): string | null {
    return localStorage.getItem(DEV_AUTH_TOKEN_KEY);
  }
}
```

```typescript
/**
 * Single responsibility: attach `Authorization: Bearer <token>` to requests going to
 * this app's own API (same origin, path under /api/) and to nothing else. The token
 * comes from AuthTokenService, never a hardcoded string, and is never committed. If
 * there is no token, the request goes out unmodified rather than with an empty header.
 *
 * Registered FIRST (outermost) in provideHttpClient(withInterceptors([...])) — see
 * app.config.ts for the full order and why — so the header is baked into the request
 * object once, before it ever reaches retryTransientGetInterceptor. RxJS `retry` re-uses
 * that same already-cloned request for every retried attempt, so every retry still
 * carries the header without this interceptor needing to run again.
 */
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthTokenService } from './auth-token.service';

function isRequestToOurApi(url: string): boolean {
  const resolved = new URL(url, location.origin);
  return resolved.origin === location.origin && resolved.pathname.startsWith('/api/');
}

export const authHeaderInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isRequestToOurApi(req.url)) {
    return next(req);
  }

  const token = inject(AuthTokenService).getToken();
  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
```

**Retry interceptor — `src/app/http/retry.interceptor.ts`**

```typescript
/**
 * Single responsibility: retry ONLY idempotent GET requests, ONLY on transient failures
 * (a network error, status 0, or a 5xx) — never a 4xx, never a non-GET verb. A 4xx will
 * not change on repeat, and retrying a non-idempotent verb (POST/PUT/PATCH) risks
 * duplicating a write, so both are excluded structurally, not just by convention.
 *
 * Cap: RETRY_MAX_ATTEMPTS = 3 total attempts (1 initial + up to 2 retries).
 * Backoff: exponential, base RETRY_BASE_DELAY_MS = 200ms, factor RETRY_BACKOFF_FACTOR =
 * 2 (attempt delays: 200ms, then 400ms).
 *
 * Registered LAST (innermost) in provideHttpClient(withInterceptors([...])) — see
 * app.config.ts — so its retries happen closest to the network and stay invisible to
 * authHeaderInterceptor and errorMappingInterceptor above it: each retry re-invokes the
 * same `next(req)` this interceptor was given, without flowing back out through those
 * outer interceptors, so they only ever see this interceptor's final settled result.
 */
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

const RETRY_MAX_ATTEMPTS = 3;
const RETRY_BASE_DELAY_MS = 200;
const RETRY_BACKOFF_FACTOR = 2;

function isTransientFailure(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
}

export const retryTransientGetInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: RETRY_MAX_ATTEMPTS - 1,
      delay: (error, retryAttempt) => {
        if (!isTransientFailure(error)) {
          return throwError(() => error);
        }
        const delayMs = RETRY_BASE_DELAY_MS * Math.pow(RETRY_BACKOFF_FACTOR, retryAttempt - 1);
        return timer(delayMs);
      },
    }),
  );
};
```

**Typed error mapping — `src/app/http/app-http-error.ts` and `src/app/http/error-mapping.interceptor.ts`**

```typescript
/**
 * Typed application error produced by errorMappingInterceptor (see
 * error-mapping.interceptor.ts) out of a failed HTTP response. Carries a friendly
 * message safe to show a user, plus per-field errors when the real response was a
 * ValidationProblemDetails.
 */
import { HttpErrorResponse } from '@angular/common/http';

export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
}

export interface ValidationProblemDetails extends ProblemDetails {
  readonly errors: Record<string, string[]>;
}

export class AppHttpError extends Error {
  constructor(
    public readonly friendlyMessage: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> | null,
    public readonly originalError: HttpErrorResponse,
  ) {
    super(friendlyMessage);
    this.name = 'AppHttpError';
  }
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  if (typeof body !== 'object' || body === null) {
    return false;
  }
  return 'title' in body || 'status' in body || 'detail' in body || 'type' in body;
}

function isValidationProblemDetails(body: unknown): body is ValidationProblemDetails {
  if (!isProblemDetails(body) || !('errors' in body)) {
    return false;
  }
  const errors = (body as { errors: unknown }).errors;
  return typeof errors === 'object' && errors !== null;
}

// The real QuotesApi (day-3/task-3/QuotesApi) never returns ProblemDetails or
// ValidationProblemDetails -- confirmed by `grep -rn "ProblemDetails|ApiController|
// AddProblemDetails|ValidationProblem"` across its entire source tree (zero matches)
// and by live capture (every observed 4xx there has an empty body). This fallback path
// is therefore the one that matters most for this API, not an edge case: a plain,
// possibly-empty 4xx must still become a sane typed error instead of throwing a raw
// HttpErrorResponse at the caller. A proxy or gateway returning plain text or HTML
// falls down this same path.
function genericMessageForStatus(status: number): string {
  switch (status) {
    case 400:
      return 'That request was invalid.';
    case 401:
      return 'You need to sign in to do that.';
    case 403:
      return "You don't have permission to do that.";
    case 404:
      return 'That could not be found.';
    case 405:
      return 'That request is not supported.';
    default:
      return status >= 500
        ? 'The server had a problem handling that request. Please try again.'
        : 'Something went wrong with that request.';
  }
}

export function mapHttpErrorToAppError(err: HttpErrorResponse): AppHttpError {
  if (err.status === 0) {
    return new AppHttpError(
      'Could not reach the server. Check your connection and try again.',
      0,
      null,
      err,
    );
  }

  const body: unknown = err.error;

  if (isValidationProblemDetails(body)) {
    return new AppHttpError(
      body.title ?? body.detail ?? 'One or more fields are invalid.',
      err.status,
      body.errors,
      err,
    );
  }

  if (isProblemDetails(body)) {
    return new AppHttpError(
      body.detail ?? body.title ?? genericMessageForStatus(err.status),
      err.status,
      null,
      err,
    );
  }

  return new AppHttpError(genericMessageForStatus(err.status), err.status, null, err);
}
```

```typescript
/**
 * Single responsibility: turn a failed HttpErrorResponse into a typed AppHttpError with
 * a friendly message (see app-http-error.ts), preserving per-field errors for a
 * ValidationProblemDetails body.
 *
 * Registered SECOND in provideHttpClient(withInterceptors([...])) — between
 * authHeaderInterceptor and retryTransientGetInterceptor — so it wraps
 * retryTransientGetInterceptor and only ever observes the FINAL settled outcome of that
 * interceptor's own internal retry loop (its retries call the shared `next` directly and
 * never re-enter this interceptor), never an intermediate failure that a later retry
 * attempt went on to fix.
 */
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { mapHttpErrorToAppError } from './app-http-error';

export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        return throwError(() => mapHttpErrorToAppError(err));
      }
      return throwError(() => err);
    }),
  );
```

**Wiring and order — `src/app/http/api-interceptors.ts` and `src/app/app.config.ts`**

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
import { authHeaderInterceptor } from './auth-header.interceptor';
import { errorMappingInterceptor } from './error-mapping.interceptor';
import { retryTransientGetInterceptor } from './retry.interceptor';

export const API_INTERCEPTORS: HttpInterceptorFn[] = [
  authHeaderInterceptor,
  errorMappingInterceptor,
  retryTransientGetInterceptor,
];
```

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { devTokenInterceptor } from './auth/dev-token.interceptor';
import { API_INTERCEPTORS } from './http/api-interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([devTokenInterceptor, ...API_INTERCEPTORS])),
  ],
};
```

### 3. The verification log

The real endpoint is `GET /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:361-362`) —
no pagination parameters exist anywhere in the handler; a request carrying `page`/`size`
is silently ignored (confirmed live, byte-identical body). Real fields, from
`Quotes/Quote.cs:5`: `{id, ownerId, text, author?}` — `ownerId` is real and required,
which the Academy's `{id, author, text}` example omits entirely. Real error shape: an
**empty body** on every 4xx reachable without a token — confirmed live (401 on an
unauthenticated POST, 405 on `GET /api/quotes/999`, both `Content-Length: 0`) — never
ProblemDetails/ValidationProblemDetails; `grep -rn
"ProblemDetails|ApiController|AddProblemDetails|ValidationProblem"` across the entire
`day-3/task-3/QuotesApi` tree returns zero matches. None of the Academy's three
assumptions (pagination, `{id, author, text}`, ProblemDetails) matched this API; all
three are pinned from the live-captured response, not from source alone, since
`GET /api/quotes` is reachable with no token — captures saved to `output/`.

The characterization test (`quotes-api-contract.spec.ts`) ran green **before** any
interceptor file existed — `output/characterization-test-green-pre-interceptor.txt`,
2026-08-26T03:29:37Z: `Test Files 7 passed (7)`, `Tests 60 passed (60)` (56 carried + 4
new). States exercised: LOADING (carried `quote-browser.spec.ts`), ERROR and EMPTY
(carried), and a 4xx surfacing as a friendly message (new
`quote-browser-friendly-error.spec.ts`, real assertion: a 401 with the real API's empty
body renders `"You need to sign in to do that."` in the DOM, not raw failure text).
Retry attempt counts, all asserted exactly: 5xx retried to the 3-attempt cap then
surfaces one error; network error retried; **4xx retried exactly once** (no second
attempt); **POST retried exactly once** (no second attempt); a GET that fails once then
succeeds surfaces no error. Backoff is virtual — `vi.useFakeTimers()` /
`vi.advanceTimersByTimeAsync()` — the suite never actually waits.

Carried tests: **56/56**, unmodified, same count as the in-place baseline. Full final
count in `day-15/task-1`: **79 tests across 12 files** (56 carried + 4 characterization +
19 new interceptor/UI tests). One carried file changed — `quote-browser.ts` — but its
carried spec needed zero changes because it configures its own interceptor-free
`HttpClient`, same as every other carried spec; see PROVENANCE.md.

The one genuine bug caught: the carried `scripts/verify-structural.mjs`'s "no
constructor-parameter injection" check did a blanket scan for `constructor(` over every
non-spec file. Adding `AppHttpError extends Error` (a plain data class, not a component
or interceptor) tripped it — real output: `FAIL: no constructor-parameter injection in
non-spec source files — .../app-http-error.ts`. Fixed by narrowing the check to scope
only to files that are actually a component or an interceptor, which is what the
requirement text actually says. (A second, earlier mistake — assuming Node's `fs` would
be available inside a spec file to read the C# source directly — is also logged in full
in `verification-log.md`.)

What breaks if the contract changes: if `GET /api/quotes` gains real pagination
parameters, `quotes-api-contract.spec.ts`'s "no pagination" assertion breaks first,
before any UI symptom. If a field is renamed (e.g. `ownerId` → `owner`), the same test's
exact-field-name assertion breaks. If the API starts returning ProblemDetails, nothing
breaks — `mapHttpErrorToAppError` already handles that shape — but the
`quotes-api-contract.spec.ts` test pinning "empty body" would need updating, since it
would then be pinning stale behaviour.

### Interpretations

- Replicated from `day-14/task-2` (not `task-1`) — it is complete and current: Day 13
  list+detail with the stale-response guard, plus both the reactive and Signal Forms
  create-quote forms; confirmed via its own `PROVENANCE.md` and a passing 6-file/56-test
  suite both in place and after this copy.
- Real endpoint `GET /api/quotes` (`Program.cs:361-362`), no pagination params, fields
  `{id, ownerId, text, author?}` (`Quotes/Quote.cs:5`), error shape is an empty 4xx body
  (zero `ProblemDetails`/`ApiController` references anywhere in the API's source) — none
  of these three match the Academy's example; each divergence stated above.
- Live-captured, not source-derived only: `GET /api/quotes` needs no token, so the
  success body, the ignored-pagination case, and two real 4xx responses (405, 401) were
  captured from a running instance and saved to `output/`.
- Retry limited to GET and to network/5xx, cap = 3 total attempts (1 initial + 2
  retries), proven by exact attempt-count assertions including both negative cases (4xx,
  POST).
- Interceptor order `[auth, errorMapping, retry]` — auth outermost so the header
  survives every retry; errorMapping wraps retry so it only ever sees retry's final
  settled outcome, never an intermediate failure a later attempt fixed; proven by
  `interceptor-order.spec.ts` and by Mutation B (swapping the order silently disables
  retry entirely).
- Fake timers (`vi.useFakeTimers()` / `vi.advanceTimersByTimeAsync()`) for all backoff
  delays — this app's test runner is vitest (`@angular/build:unit-test`), already
  configured with `vitest/globals`, so no new dependency was needed.
- Token never hardcoded or committed — `AuthTokenService` reads the same
  `devAuthToken` localStorage key the carried app already uses; tests use the obviously
  fake `'test-token'`; a structural check greps all source and captured output for any
  JWT-shaped string and finds none.
- No PATCH, no custom serialization built — the "JSON serialization + Patch" tag is a
  topic label; neither the body paragraphs nor the exercise text ask for either.
- Angular version unchanged — `package.json`/`package-lock.json` carried verbatim,
  `@angular/core` stays `^21.2.0`; `npx ng version` reports Angular CLI 21.2.21.

## What did you learn this session?

Reading Program.cs instead of trusting the Academy's example mattered: no pagination exists and the API never returns ProblemDetails, both confirmed live. Interceptor order bugs fail silently, not loudly — only a dedicated order-swap mutation test caught the reversed case.

## What would break this?

Retrying a non-GET would silently duplicate a write, which is why it's excluded structurally, not just by convention. The error mapper's ProblemDetails-parsing path has never been exercised against a live response from this API, only fixtures, since this API never actually returns one.
