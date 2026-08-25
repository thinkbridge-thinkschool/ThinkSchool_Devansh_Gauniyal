# Provenance — Day 14 Task 1

This app is a copy-forward of the Day 13 app, plus the Day 14 create-a-quote
form added on top. Verified with `diff -rq day-13/task-2 day-14/task-1`
(excluding `node_modules`, `dist`, `.angular`, `output`, and this task's own
new top-level docs) — the list below is exactly what that diff reported.

- **Source path copied from:** `day-13/task-2` (chosen over `day-13/task-1`
  because it is the later, fuller app — list-plus-detail with a
  stale-response guard, versus task-1's list-only component; see
  README.md for the full comparison).
- **Source commit hash:** `88efd46d4b0bc6f7ad13c47a5de0d3e2ac6a7bd9`
  ("Set explicit rootDir in Day 13 Task 2 tsconfig files" — the tip of
  `day-13/task-2` at copy time, confirmed with `git log -1 --format=%H -- day-13`).
- **Copy command used:**
  ```bash
  rsync -a --exclude='node_modules' --exclude='dist' --exclude='.angular' --exclude='coverage' --exclude='.git' \
    day-13/task-2/ day-14/task-1/
  ```
  followed by `npm install` in `day-14/task-1` to restore dependencies from
  the copied `package-lock.json` (no `node_modules` was copied across).

## Carried unchanged (byte-identical to day-13/task-2)

- `.editorconfig`, `.gitignore`, `.prettierrc`
- `.vscode/extensions.json`, `.vscode/launch.json`, `.vscode/mcp.json`, `.vscode/tasks.json`
- `angular.json`, `package.json`, `package-lock.json`
- `tsconfig.json`, `tsconfig.app.json`, `tsconfig.spec.json`
- `public/favicon.ico`
- `src/index.html`, `src/main.ts`, `src/styles.css`
- `src/app/app.config.ts`
- `src/app/quotes/quote.ts`
- `src/app/quotes/quote-browser/quote-browser.html`
- `src/app/quotes/quote-browser/quote-browser.css`

(`src/app/app.config.ts` was carried unchanged at first commit `6d9c69c`; a
later commit modified it — see below.)

## Modified (carried, then changed for Day 14)

- `src/app/app.ts` — now hosts `<app-create-quote-form>` alongside
  `<app-quote-browser>`; added a `justCreated` signal wiring one to the other.
- `src/app/app.html` — added the `<app-create-quote-form>` host element and a
  heading; `<app-quote-browser>` now receives `[justCreated]`.
- `src/app/app.css` — was empty in day-13/task-2; added layout spacing for
  the two stacked sections.
- `src/app/app.spec.ts` — the original carried test is untouched; one new
  test appended asserting the form is also mounted.
- `src/app/quotes/quote-api.ts` — added `createQuote()` and the
  `CreateQuoteRequest` import; `getQuotes()`/`getQuoteDetail()` untouched;
  doc comment at the top extended to document the new route.
- `src/app/quotes/quote-api.spec.ts` — the six carried tests are untouched;
  three new tests appended for `createQuote()`.
- `src/app/quotes/quote-browser/quote-browser.ts` — added a `justCreated`
  input and one `effect()` that prepends a newly created quote into
  `listData`; `ngOnInit()`, `loadList()`, and `selectQuote()` (the
  stale-response guard) are untouched, not even reformatted.
- `src/app/quotes/quote-browser/quote-browser.spec.ts` — the eight carried
  tests, including the RACE test proving the stale-response guard, are
  untouched; two new tests appended for `justCreated`.
- `scripts/verify-structural.mjs` — the six carried checks are untouched;
  one new check appended for `create-quote-form.html`'s `aria-describedby`
  resolvability.

## Newly added (Day 14 only)

- `src/app/quotes/create-quote-request.ts`
- `src/app/quotes/create-quote-form/create-quote-form.ts`
- `src/app/quotes/create-quote-form/create-quote-form.html`
- `src/app/quotes/create-quote-form/create-quote-form.css`
- `src/app/quotes/create-quote-form/create-quote-form.spec.ts`
- `brief.md`, `README.md`, `verification-log.md`, `submission.md`,
  `PROVENANCE.md` (this file) — day-13/task-2's own versions of these
  documented Day 13's task, not Day 14's, so they were removed from the copy
  and replaced with fresh ones for this task rather than left in place.
- `output/*` — fresh captured evidence for this task; day-13/task-2's own
  `output/*` was likewise removed rather than carried, since it documents
  Day 13's own test/mutation runs.

## Second commit: local manual-verification wiring (post-submission, at Devansh's request)

Devansh asked to actually see a save succeed against the real API, not just
mocked tests. Added, all clearly separated from the graded exercise:

- `proxy.conf.json` — new. Routes `/api/*` from `ng serve` to
  `http://localhost:5080`, where the real `day-3/task-3/QuotesApi` runs
  locally. Contains no secret, just a loopback URL.
- `src/app/auth/dev-token.interceptor.ts` — new. A functional
  `HttpInterceptorFn` that attaches `Authorization: Bearer <token>` only if
  a token has been manually placed in `localStorage` via the browser
  console. No token is hardcoded or committed; it is a no-op in every
  automated test (`localStorage` is empty there, and no spec file imports
  `appConfig` — each configures its own `HttpTestingController` providers
  directly).
- `src/app/app.config.ts` — modified (was carried unchanged) to register
  `devTokenInterceptor` via `withInterceptors([...])`.
- The real `day-3/task-3/QuotesApi` project itself was NOT modified — only
  its local, untracked `dotnet user-secrets` store (which lives outside the
  repo entirely, at `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`
  on this machine, never in git) was populated with a freshly generated,
  throwaway local-only `InternalCaller` credential and `InternalJwt`
  signing key, since neither existed on this machine before. See
  `README.md`, "Testing a real save locally", for what this is and how to
  use it — full credential details are in this session's reply, not
  committed anywhere.

## Third commit: real Author field, added to the actual API (Devansh's explicit request)

Devansh asked, twice and explicitly, for a real author field he could
actually save -- not a cosmetic one. This required extending the real API's
DTOs, which is a deliberate, one-time exception to the "day-3 is frozen"
rule that governed everything above, made only because he directed it.

**Blast-radius check performed before touching anything:** every project
that references `day-3/task-3/QuotesApi/QuotesApi.csproj` directly via
`ProjectReference` was enumerated (`day-4/task-2`, `task-4`, `task-5`,
`task-6`, `task-7`, `day-11/task-1`) and checked for any direct construction
of `CreateQuoteRequest`/`Quote` — none exists; the one project that POSTs to
`/api/quotes` (`day-4/task-2`'s `AuthCoverageGapTests.cs`) sends `{ text }`
only and asserts on HTTP status, not response shape. Everywhere else,
`CreateQuoteRequest`/`Quote` are separate, independent types with the same
names in unrelated projects (`day-2/task-4`, `task-6`, `task-7`,
`day-3/task-5`, `task-6`, `task-7`) that don't reference `day-3/task-3` at
all.

**day-3/task-3/QuotesApi changes (additive, backward-compatible):**
- `Quotes/QuoteRequests.cs` — `CreateQuoteRequest(string Text, string? Author = null)`.
- `Quotes/Quote.cs` — `Quote(int Id, string OwnerId, string Text, string? Author = null)`.
- `Quotes/IQuoteRepository.cs` — `Create(string ownerId, string text, string? author = null)`.
- `Quotes/InMemoryQuoteRepository.cs` — `Create()` passes `author` through.
- `Program.cs` — the POST handler passes `request.Author` through to `Create()`.

Verified: `day-3/task-3`'s own 19 tests still pass; the full repo-wide .NET
sweep (39 solutions) shows byte-identical pass/fail/skip counts to the
pre-change baseline in `output/dotnet-baseline-phase2.txt`
(`output/dotnet-post-author-change.txt`) — zero regressions anywhere.

This change lives only on the `day-14/task-1` branch's history. The
separately-pushed, already-graded `day-3/task-3` branch is a different git
ref and is untouched by it.

**Day 14 app changes for the new field:**
- `src/app/quotes/create-quote-request.ts`, `quote.ts` — added optional
  `author`, matching the DTO's optional nullable field exactly (no validator
  invented, since the server has none).
- `src/app/quotes/quote-api.ts` — doc comment updated.
- `src/app/quotes/create-quote-form/create-quote-form.ts/.html/.css` —
  added an optional "Author" input and control; included in the POST body
  only when non-blank.
- `src/app/quotes/create-quote-form/create-quote-form.spec.ts` — 3 tests
  appended for the new field; the label/tab-order A11Y tests were updated
  (not just appended to) since they now need to cover two inputs, not one.
- `src/app/quotes/quote-browser/quote-browser.html` — added an author line
  in the detail pane, template-only; `quote-browser.ts` (including
  `selectQuote()`, the stale-response guard) was NOT touched again.
- `src/app/quotes/quote-browser/quote-browser.spec.ts` — 2 tests appended.

## Not carried

`day-13/task-2/README.md`, `brief.md`, `submission.md`, `verification-log.md`,
and `output/*` were deleted from the copy immediately after the `rsync` and
before any other change, since they are Day 13's own narrative and evidence
files, not reusable app source or config. `day-13/task-2` itself was never
touched — only the copy inside `day-14/task-1` was edited.
