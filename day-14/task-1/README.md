# Day 14 Task 1 — Reactive forms + accessibility, continuing Day 13

This is NOT a fresh Angular scaffold. It is `day-13/task-2`, copied forward
byte-for-byte (see `PROVENANCE.md`), with a reactive create-a-quote form
added as a new feature alongside the existing list-plus-detail component.
`day-13/task-2` itself was never touched — verified with
`diff -rq day-13/task-2 day-14/task-1` and `git status`/`git diff --stat`
against `day-13`.

## Which Day 13 app was carried, and why

Both `day-13/task-1` and `day-13/task-2` were inspected:

| | day-13/task-1 | day-13/task-2 |
|---|---|---|
| Component | `QuoteList` — list only, with a filter and three view modes | `QuoteBrowser` — list **and** detail pane, with a stale-response guard |
| Tests | 13 (3 spec files) | 15 (3 spec files) |
| Dependencies | `@angular/core@^21.2.0`, `@angular/cli@^21.2.21`, `typescript@~5.9.2` | identical |

`day-13/task-2` is the later, fuller app — it depends on the same
`QuoteApi`/`Quote` shape `task-1` established and adds detail selection plus
a real async-race guard (`selectQuote()`'s stale-response check, proven by
the `RACE` test in `quote-browser.spec.ts`). Carried forward at commit
`88efd46d4b0bc6f7ad13c47a5de0d3e2ac6a7bd9`.

## What was carried, modified, and added

Full byte-level accounting in `PROVENANCE.md`. Summary:

- **Carried unchanged:** all config (`angular.json`, `tsconfig*.json`,
  `.gitignore`, etc.), `package.json`/`package-lock.json`, `quote.ts`, and
  `quote-browser.html`/`.css`.
- **Modified:** `quote-api.ts` (added `createQuote()`), `quote-browser.ts`
  (added a `justCreated` input + one `effect()`, `selectQuote()` untouched),
  `app.ts`/`.html`/`.css` (now host both features), the three carried spec
  files (new tests appended, none of the existing ones edited), and
  `scripts/verify-structural.mjs` (one new check appended).
- **New:** `create-quote-request.ts`, the whole `create-quote-form/`
  directory, and this task's own docs (`brief.md`, `README.md`,
  `verification-log.md`, `submission.md`, `PROVENANCE.md`, `output/`).
  Day 13's own copies of those same doc filenames were removed from the copy
  immediately after the `rsync` — they document Day 13's task, not this one.

## The real API contract this was built against

Read from `day-3/task-3/QuotesApi`, without modifying anything there:

- Route: `POST /api/quotes`, at `day-3/task-3/QuotesApi/Program.cs:364-376`,
  gated by the `CanEditQuotes` policy (scope claim `quotes.write`).
- Request DTO: `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` —
  `public sealed record CreateQuoteRequest(string Text, string? Author = null);`
- Response DTO: `day-3/task-3/QuotesApi/Quotes/Quote.cs` —
  `public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null);`
  (modeled by the carried-then-modified `quote.ts`).

**`Author` was added to the real DTO on 2026-08-25, at Devansh's explicit,
repeated request**, as a genuine extension to the API — not a cosmetic
form-only field. See "Extending the real API: the Author field" below for
the full reasoning and the blast-radius check performed before touching
`day-3/task-3` at all.

### The DTO carries no validation attributes — this is the load-bearing fact

`grep -n "Required|MaxLength|StringLength|RegularExpression|Range(" ` across
every `.cs` file in `day-3/task-3/QuotesApi` returns **zero matches**, on
either field.

This directly shapes the form:

- **No client-side maxLength was added on either field** — the API has none
  to mirror.
- **`Validators.required` was kept on `text` anyway, documented in-code as a
  client-only UX safety net**, not a mirrored server constraint.
- **`author` has no validator at all** — it mirrors the DTO's optional,
  nullable field exactly: no constraint on the server, none on the client.

## Reuse over duplication

`QuoteApi` (carried) already typed `Quote` against the real API and already
called `GET /api/quotes` twice (list and detail). `createQuote()` was added
to that same service and reuses the same `QUOTES_ENDPOINT` constant, rather
than a second, parallel HTTP service. `CreateQuoteForm` imports `Quote` and
`QuoteApi` from the existing `quotes/` folder; nothing about the model or API
base configuration was reinvented.

## Reflecting a new quote in the existing list, without touching the guard

The brief asked for a created quote to appear in the existing list "where
that is natural, without breaking the Day 13 stale-response guard."
`QuoteBrowser` gained one `input<Quote | null>()` (`justCreated`) and one
`effect()` that prepends it into `listData` (deduplicated by id) — both are
new lines added above the existing code. `ngOnInit()`, `loadList()`, and
`selectQuote()` — the method containing the actual stale-response guard —
are byte-identical to `day-13/task-2`. The existing `RACE` test in
`quote-browser.spec.ts` proves the guard still works, unmodified; two new
tests were appended proving `justCreated` behaves correctly and does not
interfere with an in-flight detail selection.

## Extending the real API: the Author field

Devansh asked, explicitly and more than once, for a real author field he
could actually save — not one that looks like it works but silently drops
on the server. The real DTO had no such field, and `day-3/task-3` had been
treated as frozen throughout this task up to that point. This is a
deliberate, one-time, directed exception to that rule, not a reinterpretation
of it — everything else under `day-3` through `day-13` remains exactly as
frozen as it was.

Before touching anything, every project depending on
`day-3/task-3/QuotesApi/QuotesApi.csproj` via a direct `ProjectReference` was
enumerated and checked: `day-4/task-2`, `task-4`, `task-5`, `task-6`,
`task-7`, and `day-11/task-1`. None of them construct `CreateQuoteRequest`
or `Quote` directly in C#; the one that POSTs to `/api/quotes`
(`day-4/task-2`'s `AuthCoverageGapTests.cs`) sends `{ text }` and asserts
only on HTTP status codes, never on response shape. Every other
`CreateQuoteRequest`/`Quote` in the repo (`day-2/task-4`, `task-6`, `task-7`,
`day-3/task-5`, `task-6`, `task-7`) is a same-named but entirely separate
type in an unrelated project.

Given that, the change made to `day-3/task-3/QuotesApi` is purely additive:
`Author` is an optional, nullable, trailing parameter with a `null` default
on both `CreateQuoteRequest` and `Quote`, so every existing caller — C# or
JSON — that never mentions `author` keeps compiling and keeps working
identically. Verified for real: `day-3/task-3`'s own 19 tests still pass,
and a full repo-wide sweep of all 39 .NET solutions shows byte-identical
pass/fail/skip counts to the pre-change baseline (`output/dotnet-baseline-phase2.txt`
vs. `output/dotnet-post-author-change.txt`) — zero regressions anywhere.

This change exists only in `day-14/task-1`'s branch history. The
already-pushed, already-graded `day-3/task-3` branch is a separate git ref
and is untouched by it — see `PROVENANCE.md` for the full file-by-file
accounting.

## What each ARIA attribute does, and what breaks without it

- **`<label for="quote-text">`** binds the visible text "Quote text" to the
  textarea. Without it, a screen reader announces only "edit text, blank."
- **`aria-invalid`** tells assistive technology the control's value is
  currently invalid, independent of anything shown visually.
- **`aria-describedby`** links the control to the element containing its
  error message, so a screen reader reads label, then value, then error, as
  one announcement when the field is focused.

### Why a dangling `aria-describedby` is invisible to visual testing

If the element an `aria-describedby` references doesn't exist in the DOM at
that moment, browsers and screen readers silently ignore the reference — no
console warning, no visual difference. This project keeps the error
`<p id="quote-text-error">` permanently in the DOM (only its text content is
conditional on `@if (showError())`), and only sets `aria-describedby` to
point at it when there is actually an error. `scripts/verify-structural.mjs`
statically confirms every `aria-describedby` target has a matching `id`
in the same template; `create-quote-form.spec.ts` asserts the referenced
element genuinely exists in the rendered DOM in both states. The mutation
check in `verification-log.md` proves this test actually fails when the bug
is reintroduced.

### Why focus management on submit matters

A keyboard- or screen-reader-only user gets no visual cue after submitting
an invalid form unless focus moves. `onSubmit()` calls
`this.textInput().nativeElement.focus()` when the form is invalid, which
both re-targets the keyboard and (because the field now carries
`aria-invalid`/`aria-describedby`) causes the error to be announced as part
of receiving focus.

### Why validators must mirror the server's constraints rather than approximate them

A validator stricter than the server blocks submissions the API would have
accepted; one looser (or invented) lets a user believe their input is fine
until a confusing SERVER-ERROR appears — or, for a constraint the server
does enforce, never appears at all if the assumed limit was wrong. Here,
since the real DTO enforces nothing, the only correct client behavior is to
enforce nothing beyond the one honestly-labeled UX safety net.

## Resolved interpretations, in full

1. **Continuation, not rebuild.** Carried from `day-13/task-2` at commit
   `88efd46d4b0bc6f7ad13c47a5de0d3e2ac6a7bd9`; `QuoteApi` extended with
   `createQuote()`, `QuoteBrowser` extended with `justCreated`, rather than
   either being duplicated.
2. **Which API and contract.** `day-3/task-3/QuotesApi`, read only until
   Devansh explicitly asked for a real `Author` field — see "Extending the
   real API" above for that one, directed exception. Every field name and
   file path above is quoted directly from source. The DTO has zero
   validation attributes on either field; reported plainly.
3. **Live calls.** None. `quote-api.spec.ts` and `create-quote-form.spec.ts`
   use `HttpTestingController` exclusively.
4. **Signal Forms vs. reactive forms.** `ReactiveFormsModule` used (stable);
   `submitting`/`serverError` held as signals, consistent with the Day 13
   app's own signal-first style.
5. **`@angular/aria`.** Not added — hand-wired ARIA on native elements is
   more directly testable and nothing in the brief requires it.
6. **Dangling `aria-describedby`.** Handled by keeping the error container
   always in the DOM and only conditionally pointing the attribute at it.
7. **Focus management.** Verified by asserting `document.activeElement`
   after a failed submit.
8. **Evidence.** Every version, count, and error string in `submission.md`
   and here comes from a captured file under `output/` or a command actually
   run in this session.

## Angular version — unchanged from the carried app

`ng version` in this session reports `Angular CLI: 21.2.21`, matching both
`day-13/task-1` and `day-13/task-2`'s `package.json`
(`@angular/cli@^21.2.21`, `@angular/core@^21.2.0`). No dependency was
upgraded; `npm install` restored the exact carried lockfile.

## How to run and test

```bash
cd day-14/task-1
npm install                     # already done; committed package-lock.json pins the tree
npx ng serve                    # http://localhost:4200/
npx ng build                    # production build
npx ng test --watch=false       # Vitest suite, no network/Docker/credentials required
node scripts/verify-structural.mjs
```

Manual screen-reader verification is Devansh's responsibility — see
`output/manual-a11y-script.md`.

## Testing a real save locally

The graded exercise deliberately never makes a live authenticated call (see
interpretation 3) — all tests use `HttpTestingController`. This section is
for manually seeing a real save succeed against the real
`day-3/task-3/QuotesApi`, which is separate from that.

`POST /api/quotes` requires a JWT with a `quotes.write` scope. No such
credential existed on this machine before this session — `dotnet user-secrets`
for that project was empty. A fresh, throwaway, local-only credential was
generated and stored there (never in the repo, never in git):

1. Start the real API (already running for you in this session, port 5080):
   ```bash
   cd day-3/task-3/QuotesApi
   ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5080
   ```
2. Restart your `ng serve` **with the proxy**, otherwise `/api/*` has nowhere
   real to go:
   ```bash
   cd day-14/task-1
   npx ng serve --proxy-config proxy.conf.json
   ```
3. Log in yourself to get a token (this repo's chat session will give you
   the exact email/password to use once, since it generated them for you) —
   either via the browser or a terminal `curl` to
   `http://localhost:4200/api/auth/login`.
4. In the browser devtools console on `http://localhost:4200/`, run:
   ```js
   localStorage.setItem('devAuthToken', 'PASTE_THE_ACCESS_TOKEN_HERE');
   ```
5. Submit the form. It will now actually save to the real API and the quote
   will appear in the list above.

This credential is local-only and expires quickly (`InternalJwt:AccessTokenLifetime`
is 15 minutes); repeat step 3-4 to get a fresh token when it does.
