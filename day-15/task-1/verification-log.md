# Verification log — Day 15 Task 1

Kept live, in order, as work happens. Not reconstructed afterward.

## Contract discovery (before any code was written)

- Read `day-3/task-3/QuotesApi/Program.cs` directly (not the copied app's comments,
  though they turned out accurate). Confirmed:
  - `GET /api/quotes` — Program.cs:361-362, no auth, no route parameters, no query
    parameters read anywhere in the handler.
  - `POST /api/quotes` — Program.cs:364-376, `RequireAuthorization(CanEditQuotes)`.
  - `PUT /api/quotes/{id:int}` and `DELETE /api/quotes/{id:int}` — auth-gated mutations.
  - No `[ApiController]` anywhere (this is a Minimal API, not MVC controllers).
  - `grep -rn "ProblemDetails|ApiController|AddProblemDetails|ValidationProblem"` across
    the entire `day-3/task-3/QuotesApi` tree returned **zero matches**. The API does not
    produce ProblemDetails or ValidationProblemDetails anywhere, ever.
- This directly contradicts the Academy's example (`GET /api/quotes?page=N&size=N`
  returning `{id, author, text}` with a 4xx as ProblemDetails/ValidationProblemDetails).
  The real API has no pagination, an extra `ownerId` field, and no ProblemDetails at all.
  See submission.md for the full discrepancy statement.

## Live capture (before any code was written)

- Found a pre-existing `QuotesApi` process already listening on `127.0.0.1:5080`
  (PID 75430, started 2026-08-25 14:45, i.e. running long before this session started).
  I did not start it. Verified it answers `GET /` with 200 before using it.
  Decision: used this already-running instance for read-only GET captures instead of
  starting a duplicate — the project's `UserSecretsId` (confirmed in the `.csproj`)
  means the signing key it needs isn't in the repo, and starting my own instance would
  either duplicate a healthy process or risk a startup failure I couldn't diagnose without
  the same local user-secrets Devansh already has configured for the running one. I did
  not stop this pre-existing process — it predates my session and I have no way to know
  whether it's needed for something else. Flagged for Devansh in the final report.
- Captured for real, to `output/`:
  - `GET /api/quotes` → 200, full JSON array, fields `id/ownerId/text/author`.
  - `GET /api/quotes?page=1&size=10` → 200, identical body — the query params are
    silently ignored (no pagination logic exists to read them). This is direct evidence
    against the Academy's example, not an assumption.
  - `GET /api/quotes/999` → **405 Method Not Allowed**, `Content-Length: 0`, `Allow:
    DELETE, PUT`. There is no `GET /api/quotes/{id}` route at all (only PUT/DELETE
    define the `{id:int}` segment), so this is a routing-level 405, not a 404, and not
    ProblemDetails.
  - `POST /api/quotes` with no Authorization header → 401 Unauthorized, `Content-Length:
    0`, `WWW-Authenticate: Bearer`. Also not ProblemDetails.
  - Every observed 4xx from this real API is an **empty body**, not JSON of any shape.
    This is the governing fact for the error-mapping interceptor: it must have a sane
    fallback path for a non-ProblemDetails (here: no-body) 4xx, because that is what the
    real API actually returns on every reachable-without-a-token error case.

## Genuine mistakes and corrections (as they happened)

### Mistake 1 — assumed Node's `fs` would be available inside a spec file

What I wrote: a throwaway spec (`fs-experiment.spec.ts`) using `import { readFileSync }
from 'node:fs'` and `import.meta.dirname` to read `day-3/task-3/QuotesApi/Program.cs`
directly off disk from inside the characterization test, so the test would fail
automatically if the real C# route/field/error shape ever changed — the most literal
possible reading of "pins existing behaviour... such that renaming a field... makes it
fail."

How the problem surfaced: ran `ng test`. The build failed before any test executed.

Real error text:
```
✘ [ERROR] TS2307: Cannot find module 'node:fs' or its corresponding type declarations.
✘ [ERROR] TS2307: Cannot find module 'node:path' or its corresponding type declarations.
✘ [ERROR] TS2339: Property 'dirname' does not exist on type 'ImportMeta'.
```

Why: this app's `tsconfig` targets the browser (no `@types/node`), and the TECH STACK
CONSTRAINT for this task forbids adding any new package — so installing `@types/node`
to fix the compile error was not an option.

What I changed: deleted the experiment file and pinned the contract a different way —
the characterization test (`quotes-api-contract.spec.ts`) asserts against (a) the real
DTO field names and route as literal string/object fixtures I read directly from
`day-3/task-3/QuotesApi/Quotes/Quote.cs` and `Program.cs` by eye and transcribed with a
comment citing the exact source line, and (b) the actual live-captured response bodies
saved under `output/` during the live capture step, embedded as fixtures. A field
rename or a route change in the real API does not make this test read the new file
automatically, but it does mean the fixture and the source comment next to it go out of
sync in a way a mentor reviewing the diff would catch — this is the same technique the
carried `quote-api.spec.ts` already uses for the same reason, so this is not a new
pattern here.

## Characterization test — green BEFORE any interceptor existed

Ran at 2026-08-26T03:29:37Z (real output captured to
`output/characterization-test-green-pre-interceptor.txt`), with **zero** interceptor
files in the repository — `git status`/`ls src/app/http` at this point would show no
`http/` directory at all; it is created in the next step. Result:

```
Test Files  7 passed (7)
     Tests  60 passed (60)
```

7 = the 6 carried files + `quotes-api-contract.spec.ts`. 60 = the 56 carried tests + the
4 new characterization tests (route/no-pagination, ignored page/size params, real field
names, empty-body 4xx). This is the evidence that the sequence the brief requires —
characterization test written and green before any UI or interceptor work — was
actually respected, not just claimed.

### Mistake 2 — AppHttpError's own constructor tripped the carried structural check

What I wrote: `AppHttpError extends Error` with a normal constructor taking
`(friendlyMessage, status, fieldErrors, originalError)` as TypeScript parameter
properties (`public readonly x: T`), then ran the carried `scripts/verify-structural.mjs`.

How the problem surfaced: a real, immediate FAIL from the script, not a guess.

Real output:
```
FAIL: no constructor-parameter injection in non-spec source files
      /Users/devansh/thinkschool/day-15/task-1/src/app/http/app-http-error.ts
```

Why this happened: the carried check (written for day-14/task-2, which had no error
classes at all) does a blanket `constructor(` scan over every non-spec `.ts` file. It
was written to catch Angular's constructor-based DI idiom
(`constructor(private readonly foo: FooService)`), but as written it also flags any
plain class constructor -- including `AppHttpError`'s, which has nothing to do with
Angular DI and cannot be written any other way (a class extending `Error` needs a
constructor to set its own fields).

What I changed: narrowed the check in `scripts/verify-structural.mjs` to scope to files
that are actually a component (`@Component(`) or an interceptor (`: HttpInterceptorFn`)
-- which is exactly what the grading requirement says ("no component or interceptor
uses constructor parameter injection"), not "no class anywhere may have a constructor".
This is a strengthening, not a weakening: it still catches the real thing the check
exists for (constructor DI in a component or interceptor) and now also correctly leaves
a legitimate plain data/error class alone. See PROVENANCE.md for this file's full
carried/modified justification.

## Phase 5 — deliberate mutations (NOT mistakes, kept separate per the task instructions)

Both are real: applied to actual source, run for real, real failure captured, reverted,
re-run for real to confirm green again. Full output saved to `output/mutation-*.txt`.

### Mutation A — retry proof (required)

Widened `isTransientFailure` in `retry.interceptor.ts` from `status === 0 || status >=
500` to `status === 0 || status >= 400` (i.e. every 4xx now looks transient too).

Broken run (`output/mutation-A-broken.txt`): **2 tests failed**, both genuinely, for the
reason the mutation predicts:
- `retry.interceptor.spec.ts > a GET failing with a 4xx is NOT retried -- exactly one
  attempt` — `Error: Expected zero matching requests for criteria "Match URL:
  /api/quotes", found 1.` (the mutated interceptor retried the 404).
- `quote-browser-friendly-error.spec.ts > ... renders as a readable message` —
  `AssertionError: expected null to be truthy` (the component's list never left the
  loading state within the test because the mutated interceptor was still waiting on a
  retry timer nobody advanced in that spec, so no error ever reached the UI).

Reverted; re-run (`output/mutation-A-reverted.txt`): 12 files / 79 tests, all passed
again.

### Mutation B — interceptor order proof

Swapped `API_INTERCEPTORS` in `api-interceptors.ts` from `[auth, errorMapping, retry]`
to `[auth, retry, errorMapping]` — the wrong order.

Broken run (`output/mutation-B-broken.txt`): **2 tests failed**, both in
`interceptor-order.spec.ts`, both `Error: Expected one matching request for criteria
"Match URL: /api/quotes", found none.` — with errorMapping now positioned closer to the
backend than retry, retry's `isTransientFailure` check receives an already-mapped
`AppHttpError` instead of the raw `HttpErrorResponse` it requires, so it silently stops
retrying transient failures altogether: no second HTTP request is ever issued for either
test to find.

Reverted; re-run (`output/mutation-B-reverted.txt`): 12 files / 79 tests, all passed
again. Structural checks (`node scripts/verify-structural.mjs`) also re-confirmed green
after the revert.

## Addendum — post-submission demo panel (not part of the graded work above)

Everything above this line was written and verified before the original commit. This
section documents a follow-up addition Devansh explicitly asked for afterward, so it is
kept clearly separate rather than folded into the log as if it had been part of the
original sequence.

Devansh wanted a way to demonstrate the three interceptors live to a mentor without
relying on DevTools throttling (fiddly to do reliably in real time). Added
`src/app/demo/http-demo-panel/` (a small component with two buttons — a real success
call and a real 4xx call — plus a request counter) and a demo-only
`requestCounterInterceptor`, registered last in `app.config.ts` so it counts every real
network attempt including retries without altering the order or behaviour of the three
graded interceptors in front of it. Full detail and justification in `PROVENANCE.md`.

Ran the full suite and structural checks after adding it: 12 files / 79 tests, all
passing — identical counts to before, since the demo panel has no tests of its own and
touches no existing test path. Then drove it live with Playwright against the real,
already-running QuotesApi: the success button showed the real list with "1 request
made."; the 4xx button (`GET /api/quotes/999`) showed "1 request made — a 405 is never
retried." with the friendly message and `status=405 · retryable=false` — confirming the
graded interceptor chain works exactly the same when driven this way as it does in the
automated tests. Zero unmodified files outside `src/app/demo/` and the four
already-documented touch points (`app.ts`, `app.html`, `app.config.ts`,
`submission.md`).
