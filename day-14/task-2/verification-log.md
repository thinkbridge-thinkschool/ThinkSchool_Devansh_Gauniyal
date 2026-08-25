# Verification log — Day 14 Task 2

Kept as work happened, not reconstructed afterward. Genuine mistakes and
deliberate mutations are in clearly separate sections, per the task's
instructions.

## 1. Which app to replicate

Only `day-14/task-1` exists as a candidate — it carries the Day 13 list-
plus-detail app and adds the reactive create-a-quote form, at commit
`32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba`. It is the latest and only
candidate app; no comparison to a competing app was needed.

## 2. Signal Forms Availability Gate — real evidence, not memory

`node_modules/@angular/forms/package.json`'s `exports` map (this project's
actual installed `@angular/forms@21.2.21`):

```json
"./signals": {
  "types": "./types/signals.d.ts",
  "default": "./fesm2022/signals.mjs"
}
```

Real import path: `@angular/forms/signals`. Symbols used, read directly from
`node_modules/@angular/forms/types/signals.d.ts` and the
`_structure-chunk.d.ts` file it re-exports from (not assumed): `form`,
`schema`, `submit`, `required`, `FormField` (directive, selector
`[formField]`), `FormRoot` (directive, selector `form[formRoot]`), and the
`Field`/`FieldState`/`FieldTree` types. `FieldState` exposes `.value`,
`.touched`, `.dirty`, `.valid`, `.invalid`, `.errors`, `.submitting`,
`.markAsTouched()` — all `@experimental` since `21.0.0` per the JSDoc
annotations in the type file itself. No flag gating, no missing symbols —
the gate passed cleanly; this section records what was actually found, not
that a workaround was needed.

## 3. Re-reading the real contract (Endpoint Conflict Check)

`day-3/task-3/QuotesApi/Program.cs:364-376` — `app.MapPost("/api/quotes", ...)`.
Matches the Academy's `/api/quotes` shorthand exactly; no discrepancy.
`day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs`:
`public sealed record CreateQuoteRequest(string Text, string? Author = null);`
— re-confirmed zero validation attributes anywhere in the project
(`grep -n "Required|MaxLength|StringLength|RegularExpression|Range(" ` across
every `.cs` file, zero matches). This agrees exactly with what Day 14 Task 1
already recorded; nothing had changed.

## 4. Genuine mistakes made while building, in the order they happened

**Mistake 1 — a real TypeScript compile error.** `quoteForm` was first
declared `protected`, matching the reactive form's `protected readonly form`.
Writing the first state test (`component.quoteForm.text().dirty()`) failed
the build with a real compiler error:

```
✘ [ERROR] TS2445: Property 'quoteForm' is protected and only accessible
within class 'CreateQuoteFormSignals' and its subclasses. [plugin
angular-compiler]
    src/app/quotes/create-quote-form-signals/create-quote-form-signals.spec.ts:56:21
```

Fix: dropped `protected`, since pristine/dirty/touched have no DOM-attribute
equivalent the way validity does via `aria-invalid` — the state tests
genuinely need direct access to the `FieldTree`, unlike the reactive form's
tests, which only ever inspect the DOM.

**Mistake 2 — a wrong assumption about which real DOM event marks a field
touched.** The test helper for "blur a control" dispatched `focusin` then
`focusout` (a common bubbling-event testing pattern). Four tests failed for
real:

```
FAIL ... TOUCHED: focusing then blurring a control marks it touched, and only that one
AssertionError: expected false to be true

FAIL ... ERROR DISPLAY: the error message renders only after touched, never on a pristine form
AssertionError: expected '' to be 'Quote text is required.'

FAIL ... FAILED SUBMIT: ...
FAIL ... A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state
```

Rather than guess again, read the actual compiled package source:

```
grep -n "'blur'" node_modules/@angular/forms/fesm2022/signals.mjs
881:  host.listenToDom('blur', () => parent.state().markAsTouched())
```

Fix: the test helper dispatches the real `focus`/`blur` events instead.
Three of the four failures above were downstream of this one root cause;
fixing it alone resolved three of them.

**Mistake 3 — an async-timing assumption in a zoneless app.** The
`FAILED SUBMIT` test still failed after fixing Mistake 2:

```
AssertionError: expected true to be false
❯ expect(component.quoteForm().submitting()).toBe(false);
```

`await fixture.whenStable()` was added first, assuming it would wait for the
in-flight submission the same way it does elsewhere in this app's (reactive,
Observable-native) tests — and the test still failed identically. The
submission `action` bridges `QuoteApi.createQuote()`'s Observable to a Promise
by hand (`new Promise((resolve, reject) => { ...subscribe... })`); this
zoneless app's `ApplicationRef` pending-task tracking has no visibility into
a manually constructed Promise, so `whenStable()` resolved before that
Promise's microtasks had actually run. Fix: `await new Promise((resolve) =>
setTimeout(resolve, 0))` — a macrotask tick — reliably drains it. Real
result after this fix: all 13 new tests passed.

**Mistake 4 — unnecessary code, caught by testing the assumption rather than
trusting it.** Believing `submit()`'s `onInvalid` callback would behave like
a bare validity check (matching the reactive form, which needs an explicit
`form.markAllAsTouched()` call to make errors visible on a failed submit),
two `markAsTouched()` calls were added inside `onInvalid`. To verify this
was actually necessary rather than copied out of habit, both calls were
deleted and the suite re-run: all 56 tests still passed, including the ones
asserting `touched()` and error visibility after a failed submit. Fix: the
calls were removed rather than left in as harmless-but-unnecessary
defensive code — `submit()` already leaves attempted fields touched on an
invalid submission. This is recorded as a genuine finding about the preview
API's actual behavior, not a bug that broke anything; see `comparison.md`
for what it means for the two forms' relative simplicity.

No other correction of substance was needed. The initial `ng build` (before
any tests were written) succeeded on the first attempt.

## 5. States exercised, real test names, real counts

`npx ng test --watch=false` (Vitest, the carried app's own runner — no
Karma added): **56 passed (56)**, 6 spec files. Split:
**40 carried** (identical count to `day-14/task-1`'s in-place baseline,
across `app.spec.ts`, `quote-api.spec.ts`, `create-quote-form.spec.ts`, and
`quote-browser.spec.ts` — including the `RACE` test proving the Day 13
stale-response guard still works, untouched) + **16 new**
(`create-quote-form-signals.spec.ts` ×13, `create-quote-form-parity.spec.ts`
×3).

Signal Forms states, by real test name:
- `PRISTINE: a freshly rendered form reports pristine (not dirty) on both fields and shows no error`
- `DIRTY: changing the text value flips text to dirty without affecting author`
- `TOUCHED: focusing then blurring a control marks it touched, and only that one`
- `VALIDATORS FIRING: an empty text field reports the real required error...`
- `ERROR DISPLAY: the error message renders only after touched, never on a pristine form`
- `CLEAN SUBMIT: a valid form issues a POST to the real route with the real field names and exact casing`
- `SUBMITTED (invalid): submitting a completely empty form marks both fields touched, does not call the API, and moves focus to the first invalid field`
- `SUBMITTED (invalid, text valid but author blank): focus moves to author instead`
- `FAILED SUBMIT: a rejected POST surfaces the error and does not leave the form stuck submitting`

No state named by the task (pristine, dirty, touched, validators firing,
error display, clean submit, failed submit) was inexpressible in the preview
API — all seven were asserted directly. `submitted`, specifically, has no
single boolean field the way `dirty`/`touched` do; it is expressed here
through `onInvalid` firing (for a failed attempt) and through
`submitting()`'s transition (for an in-flight/completed one) — see
README.md, "pristine/dirty/touched/submitted", for the full mapping.

Parity, by real test name: `PARITY: both forms send the identical real
field names and casing for the same input`, `PARITY: both forms reject the
same invalid input (both blank) without calling the API`,
`CONTRACT: a field the real API doesn't have (e.g. 'title') appears in
neither payload`.

## 6. Mutation check (required, real output, kept separate from section 4)

Deliberate breaks, not genuine mistakes. Full captured output in
`output/mutation-A-broken.txt`, `output/mutation-A-reverted.txt`,
`output/mutation-B-broken.txt`, `output/mutation-B-reverted.txt`.

**Mutation A — the Signal Forms proof: remove the `required()` call on
`text`.** Real result: `1 failed | 5 passed (6)` test files, `4 failed | 52
passed (56)` tests — `VALIDATORS FIRING`, `ERROR DISPLAY`,
`SUBMITTED (invalid)` (focus landed on the now-valid-by-default author field
instead of text), and the A11Y `aria-describedby` test. Reverted; re-ran;
back to `56/56`.

**Mutation B — the accessibility proof: dangling `aria-describedby`.**
Changed the conditional `[attr.aria-describedby]` to an unconditional,
always-present `aria-describedby="quote-text-signals-error"`, while wrapping
the entire `<p id="quote-text-signals-error">` element in `@if (showError(...))`.
Real result: `1 failed | 5 passed (6)` test files, `3 failed | 53 passed (56)`
tests, including `AssertionError: expected null to be truthy` on the
DOM-resolution test — the accessibility test genuinely catching a genuinely
dangling reference. Reverted; re-ran; back to `56/56` and all 10 structural
checks `PASS`.

## 7. What breaks if the quote contract changes

- Renaming `Text` or `Author` on the DTO breaks the wire format silently in
  both forms identically (both send `{ text, author }` today; neither
  notices a rename at compile time).
- A genuine future `[MaxLength]` on either field would be under-enforced by
  both forms equally, since neither enforces a length today.
- If the preview API's shape changes in a future Angular minor (it is
  `@experimental` throughout, `21.0.0`/`21.2.0`), the Signal Forms version
  specifically — not the reactive one — may stop compiling; see
  submission.md, "What would break this?"
