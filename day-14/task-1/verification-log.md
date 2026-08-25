# Verification log — Day 14 Task 1

Kept as work happened, not reconstructed afterward. Genuine mistakes and
deliberate mutations are in clearly separate sections, per the task's
instructions.

## 1. Which Day 13 app to carry

Inspected both. `day-13/task-1` has `QuoteList` (a single list component with
a filter and three view modes — 13 tests, 3 spec files). `day-13/task-2` has
`QuoteBrowser` (list-plus-detail, with a stale-response guard proven by a
RACE test — 15 tests, 3 spec files). Both declare identical dependency
versions in `package.json` (`@angular/core@^21.2.0`, `@angular/cli@^21.2.21`,
`typescript@~5.9.2`). Task-2 is the later, fuller app (it depends on the same
`QuoteApi`/`Quote` shape task-1 established, plus adds detail selection and
the race guard) — carried forward, per PROVENANCE.md.

## 2. Reading the real contract, before writing any form code

`day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs`:

```csharp
public sealed record CreateQuoteRequest(string Text);
```

`grep -n "Required|MaxLength|StringLength|RegularExpression|Range(" ` across
every `.cs` file in `day-3/task-3/QuotesApi` returned **zero matches**. The
DTO has exactly one field, `Text`, and **no validation attributes of any
kind**. This is the same finding as the prior attempt at this task, reread
and reconfirmed here rather than assumed.

Consequences for the form, same reasoning as before:

- **No maxLength validator was added** — inventing one would misrepresent
  the API's actual (nonexistent) limit.
- **`Validators.required` was kept, documented in-code as a client-only UX
  safety net**, not a mirrored server constraint.
- **Exactly one field, `text`.** No `author` or other invented field.

The response DTO (`day-3/task-3/QuotesApi/Quotes/Quote.cs`) and the route
(`day-3/task-3/QuotesApi/Program.cs:364-376`, gated by `CanEditQuotes`) were
read the same way and are cited directly in `quote-api.ts`.

## 3. Genuine mistakes made while building

**None of substance.** The carried copy built and its existing 15 tests
passed, unchanged, immediately after the `rsync` + `npm install` (Step 4B),
before any new code was written. After adding `createQuote()`,
`create-quote-request.ts`, `CreateQuoteForm`, the `justCreated` wiring on
`QuoteBrowser`, and `App`'s host template, the full suite (33 tests, 4 spec
files) passed on the first real `ng build` / `ng test --watch=false` run —
see `output/final-build.txt` and `output/final-test-run.txt`. No compiler
error, no failing test, no structural-check failure occurred during
development that required a correction.

The fifth candidate the task names — "breaking something Day 13 already did
while adding the form" — is exactly what Step 4B and the carried-tests split
in Phase 5 exist to catch, and it did not happen here: `selectQuote()` (the
stale-response guard) was never touched, only new lines were added above it,
and the RACE test in `quote-browser.spec.ts` (`RACE: discards a stale detail
response when it arrives out of order...`) still passes unmodified. Two new
tests were appended, never inserted into or edited within the existing ones,
so a diff of that file shows only additions.

What would have caught a mistake had one occurred, concretely:

- Breaking the stale-response guard would have failed the untouched RACE
  test in `quote-browser.spec.ts`.
- An invented field or a mismatched field name would have failed the
  CONTRACT tests in `create-quote-form.spec.ts` and `quote-api.spec.ts`
  (`Object.keys(req.request.body)).toEqual(['text'])`).
- A dangling `aria-describedby` would have failed the accessibility
  resolution test — proven below in the mutation check.

## 4. States exercised, with real test names and real counts

All four states named by the task, in
`src/app/quotes/create-quote-form/create-quote-form.spec.ts`:

- `EMPTY: a freshly rendered form shows no error message and no aria-invalid`
- `INVALID: submitting empty marks the field invalid, renders the error, sets aria-invalid, and does not call the API`
- `SUBMITTING: the busy state is active in flight and the form cannot be double-submitted`
- `SERVER-ERROR: a failing POST surfaces a visible, announced error and leaves the form usable again`

Real result, captured to `output/final-test-run.txt`:

```
Test Files  4 passed (4)
     Tests  33 passed (33)
```

Split: **15 carried** (`app.spec.ts` ×1, `quote-api.spec.ts` ×6,
`quote-browser.spec.ts` ×8 — identical count and identical test bodies to
the in-place Phase 2 baseline for `day-13/task-2`) + **18 new** (`app.spec.ts`
×1, `quote-api.spec.ts` ×3, `quote-browser.spec.ts` ×2,
`create-quote-form.spec.ts` ×12).

## 5. Accessibility verification — automated vs. manual, honestly split

**Automated (this agent's responsibility), real assertions, real results:**

- `A11Y: the input has a label whose for matches its id` — passed.
- `A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state` — passed.
- `A11Y: focus moves to the invalid field after a failed submit` — passed
  (asserts `document.activeElement`, fixture attached to `document.body`).
- `A11Y: no element has a positive tabindex` — passed.
- `A11Y: the textarea and submit button are reachable in the tab order in the initial state` — passed.
- `scripts/verify-structural.mjs` additionally statically confirms every
  `aria-describedby` target in `create-quote-form.html` has a matching
  literal `id="..."` in the same file, alongside the six carried checks
  (no `NgModule`, no constructor injection, no `any`, strict mode,
  `@for` tracks `quote.id`, no Zone.js). Real output,
  `output/structural-check-final.txt`: all 8 checks `PASS`.

**Manual (Devansh's responsibility) — not done by this agent, marked PENDING:**

Screen-reader / axe verification has not happened yet. See
`output/manual-a11y-script.md` for the exact script.

## 6. Mutation check (required, real output, kept separate from section 3)

Deliberate breaks, not genuine mistakes. Full captured output in
`output/mutation-A-broken.txt`, `output/mutation-A-reverted.txt`,
`output/mutation-B-broken.txt`, `output/mutation-B-reverted.txt`.

**Mutation A — remove the one real validator.**
Changed `validators: [Validators.required]` to `validators: []` in
`create-quote-form.ts`. Real result: `1 failed | 3 passed (4)` test files,
`11 failed | 22 passed (33)` tests — the INVALID/SUBMITTING/SERVER-ERROR/
CONTRACT/A11Y tests that depend on the field actually being invalid when
empty, plus a real cascade once an unflushed `HttpTestingController` request
broke `TestBed` state for the rest of that spec file. Reverted; re-ran; back
to `33 passed (33)`.

**Mutation B — the accessibility proof: dangling `aria-describedby`.**
Changed the conditional `[attr.aria-describedby]="showError() ? 'quote-text-error' : null"`
to an unconditional, always-present `aria-describedby="quote-text-error"`,
while wrapping the entire `<p id="quote-text-error">...</p>` element (not
just its text) in `@if (showError())`. In the untouched/valid state the
attribute now points at an id that does not exist anywhere in the DOM. Real
result: `1 failed | 3 passed (4)` test files, `2 failed | 31 passed (33)`
tests:

```
FAIL ... EMPTY: a freshly rendered form shows no error message and no aria-invalid
AssertionError: expected 'quote-text-error' to be null

FAIL ... A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state
AssertionError: expected null to be truthy
```

The second failure is the accessibility resolution test genuinely catching a
genuinely dangling reference. Reverted; re-ran; back to `33 passed (33)` and
all 8 structural checks `PASS`.

## 7. Post-submission: a real Author field, added to the actual API

Devansh tried to save a quote via the running app and hit
"You are not authorized to create quotes." — the correct SERVER-ERROR
behavior for a genuinely unauthenticated request, not a bug. That led to
setting up a local dev credential and a proxy so the save could actually be
exercised (see section 6 in `README.md`, "Testing a real save locally"). He
then asked, twice and explicitly, for a real author field — not a cosmetic
one that would silently do nothing.

This meant extending `day-3/task-3/QuotesApi`, which every document in this
task up to that point had treated as frozen. Before making the change, every
project that references that project directly (`day-4/task-2`, `task-4`,
`task-5`, `task-6`, `task-7`, `day-11/task-1`) was checked for any code that
constructs `CreateQuoteRequest`/`Quote` — none does, so an additive, optional
`Author` field (default `null`, no validation attribute) could not break
them. Real verification, not assumption: `day-3/task-3`'s own 19 tests still
pass, and the full 39-solution repo-wide .NET sweep shows byte-identical
pass/fail/skip counts to the pre-change baseline
(`output/dotnet-baseline-phase2.txt` vs. `output/dotnet-post-author-change.txt`).

This is reported here as a genuine, real change made mid-session at explicit
direction — not a mistake, and not something to hide. It does mean the
"the DTO has exactly one field, don't invent a second one" framing that
drove sections 1-6 above is now a historical fact about the *original*
contract, not the current one. Both are true and both are recorded: the
form was built honestly against the contract as it existed, and it was
honestly extended, on the record, when asked.

Angular-side tests added for the new field, all real, all passing:
`AUTHOR: an unfilled author is omitted from the payload entirely...`,
`AUTHOR: a filled author is included in the payload under the real field
name`, `AUTHOR: is never required...` (`create-quote-form.spec.ts`), and
`AUTHOR: renders the author line...` / `AUTHOR: omits the author line...`
(`quote-browser.spec.ts`). Full suite after this change: **38 passed (38)**.

## 8. Post-submission: Author made compulsory, and a visual pass

Devansh asked for `author` to be required rather than optional, and for the
page to look cleaner. Before making `author` required, checked whether doing
so server-side would break anything real: `day-3/task-3/QuotesApi.Tests`'
`AuthIntegrationTests.CreateQuote_WithWritePolicy_ReturnsOk` posts
`{ text = "A new quote" }` with no author and asserts `200 OK` — making the
server require it would have broken that already-passing, already-graded
test. So `author` was made required the same way `text` already was: a
documented client-only rule (`Validators.required`), not a mirrored server
constraint — the server DTO stays optional, unchanged. Updated the
`showError()` logic to take a field name, added focus-to-first-invalid
across two required fields (text first, then author, matching DOM order),
and replaced the now-obsolete "author is optional" tests with "author is
required" ones rather than leaving them contradicting the new behavior.
Full suite after this change: **39 passed (39)**, structural checks 8/8.

The visual redesign (`app.css`, `quote-browser.css`, `create-quote-form.css`,
`dev-login.css`, `styles.css`) touched only CSS/template classes, no
component logic — verified with the same 39/39 test run before and after.

## 9. What breaks if the quote contract changes

- If `CreateQuoteRequest.Text` were renamed (e.g. to `Content`), `quote-api.ts`
  and `create-quote-form.ts` would keep sending `{ text: ... }` — the server
  would bind it to nothing, and nothing on the client would notice, since
  there is no shared type between the two projects.
- If the DTO gained a genuine `[Required]`/`[MaxLength]` attribute later,
  this form's single `Validators.required` would still be honest for
  "required" but would silently under-enforce any new length limit — a user
  could submit a too-long quote that the client accepts and the server now
  rejects, surfacing only as a SERVER-ERROR.
- A new required field on the DTO (e.g. an `author`) would need a new
  control, label, and `aria-describedby` id, and a new CONTRACT test
  asserting its presence — none of that exists today because the real DTO
  doesn't have it yet.
