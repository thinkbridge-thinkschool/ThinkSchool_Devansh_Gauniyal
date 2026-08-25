## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-14/task-2/day-14/task-2

## Notes for mentor

This replicates day-14/task-1 at commit `32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba` and adds a Signal Forms version of the same form alongside the reactive one; day-13 and day-14/task-1 are unchanged.

### 1. The brief

Take the Angular 21 app from day-14/task-1 — the quotes list-plus-detail with the reactive create-a-quote form — and copy it into day-14/task-2 untouched. Do not modify day-14/task-1 or anything from Day 13.

Inside day-14/task-2, rebuild the SAME create-a-quote form a second time using the Signal Forms preview API, alongside the existing reactive-forms version. Keep both. They must post to the same real endpoint with the same real fields, so the two can be compared directly.

The API is the QuotesApi at day-3/task-3/QuotesApi. Read the POST endpoint's route, its request DTO and every validation attribute on that DTO. The form shape and every validator must come from those actual fields and constraints. Do not invent a field, do not omit a required one, and do not pick a limit that differs from the API's.

Requirements:
- the Signal Forms version must use the actual Signal Forms API as it exists in the installed Angular 21 package — verify the import path and symbols from node_modules, do not assume them
- the same four states handled and observable: pristine, dirty, touched, and submitted; validators firing; error display; a clean submit and a failed submit
- validators matching the real API constraints exactly, each with a comment naming the constraint and the DTO file it came from
- inject() for every dependency, no constructor injection; no `any` anywhere
- do not degrade the existing reactive form, the list, the detail, or the Day 13 stale-response guard
- where the preview API cannot do something the reactive version does, say so plainly in code comments and in the comparison — do not hand-roll a workaround and present it as Signal Forms doing the work

Also write a short, honest comparison: where Signal Forms is simpler than reactive forms in this specific form, and where it is still rough. Ground every claim in something you actually hit while building, not in general reputation. Do not claim parity you did not observe.

Change nothing else. No refactors, no renames, no dependency upgrades, no restyling.

### 2. The agent's output

Real import path and symbols used: **`@angular/forms/signals`** — `form`, `schema`, `submit` (used via the `FormRoot` directive), `required`, `FormField` (directive, `[formField]`), `FormRoot` (directive, `[formRoot]`), `Field` (type). Verified in `node_modules/@angular/forms/package.json`'s `exports` map and `node_modules/@angular/forms/types/signals.d.ts` — every exported symbol there carries `@experimental 21.0.0` (or `21.2.0`) in its own JSDoc.

`src/app/quotes/create-quote-form-signals/create-quote-form-signals.ts`:

```ts
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
// Real import path and symbols, verified by reading the installed package's own
// type definitions (node_modules/@angular/forms/types/signals.d.ts and
// _structure-chunk.d.ts) rather than assumed from memory -- see
// verification-log.md and README.md, "The real Signal Forms surface".
import { FormField, FormRoot, form, required, schema } from '@angular/forms/signals';
import type { Field } from '@angular/forms/signals';
import { QuoteApi } from '../quote-api';
import type { Quote } from '../quote';

interface CreateQuoteModel {
  text: string;
  author: string;
}

// The real CreateQuoteRequest DTO (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs)
// carries no validation attributes on either field -- both are optional, nullable
// server-side. Both `required()` calls below are client-only UX safety nets, the
// same directed decision already documented on the reactive version
// (create-quote-form.ts): `text`'s because a blank quote is meaningless to save,
// `author`'s because Devansh asked for it to be compulsory on the form.
const createQuoteSchema = schema<CreateQuoteModel>((path) => {
  required(path.text, { message: 'Quote text is required.' });
  required(path.author, { message: 'Author is required.' });
});

@Component({
  selector: 'app-create-quote-form-signals',
  imports: [FormField, FormRoot],
  templateUrl: './create-quote-form-signals.html',
  styleUrl: './create-quote-form-signals.css',
})
export class CreateQuoteFormSignals {
  private readonly quoteApi = inject(QuoteApi);
  private readonly textInput =
    viewChild.required<ElementRef<HTMLTextAreaElement>>('textInput');
  private readonly authorInput =
    viewChild.required<ElementRef<HTMLInputElement>>('authorInput');

  private readonly model = signal<CreateQuoteModel>({ text: '', author: '' });

  protected readonly serverError = signal<string | null>(null);
  protected readonly submittedQuote = signal<string | null>(null);

  readonly quoteCreated = output<Quote>();

  // `form()` and `schema()` are the real Signal Forms entry points (see import
  // comment above). `<form [formRoot]="quoteForm">` in the template is what
  // actually triggers submission -- it prevents the native submit event and
  // calls the framework's own `submit()` using the `submission` options below;
  // no manual (ngSubmit)/(submit) handler was written. The action posts
  // through the SAME QuoteApi service the reactive form uses.
  //
  // Not `protected`: pristine/dirty/touched have no DOM-attribute equivalent
  // the way validity does via aria-invalid, so the state tests assert on
  // this FieldTree directly rather than only through the rendered DOM.
  readonly quoteForm = form(this.model, createQuoteSchema, {
    submission: {
      action: async (field) => {
        this.serverError.set(null);
        const value = field().value();
        try {
          const quote = await new Promise<Quote>((resolve, reject) => {
            this.quoteApi.createQuote({ text: value.text, author: value.author }).subscribe({
              next: resolve,
              error: reject,
            });
          });
          this.submittedQuote.set(quote.text);
          this.model.set({ text: '', author: '' });
          this.quoteCreated.emit(quote);
        } catch (error) {
          const status = error instanceof HttpErrorResponse ? error.status : 0;
          this.serverError.set(
            status === 401 || status === 403
              ? 'You are not authorized to create quotes.'
              : 'The quote could not be saved. Please try again.',
          );
        }
        // No field-level submission errors to attach -- server errors surface
        // through the serverError banner above instead, to match the reactive
        // form's UX for a fair comparison. See comparison.md.
        return undefined;
      },
      onInvalid: () => {
        // No manual markAsTouched() calls here -- an earlier draft assumed
        // Signal Forms would need the same explicit
        // form.markAllAsTouched() the reactive version calls on a failed
        // submit, added markAsTouched() calls on each field to match, and
        // then genuinely tested the assumption by deleting them: every
        // touched/error-display test still passed. submit() already marks
        // attempted fields touched on an invalid submission; see
        // verification-log.md for the real before/after test run this is
        // based on.
        if (this.quoteForm.text().invalid()) {
          this.textInput().nativeElement.focus();
        } else {
          this.authorInput().nativeElement.focus();
        }
      },
    },
  });

  // Signal Forms has no `pristine` flag; the field state exposes `dirty`
  // instead (see README.md, "pristine/dirty/touched/submitted"). Error
  // display uses the same touched-based rule as the reactive form.
  protected showError(field: Field<string>): boolean {
    const state = field();
    return state.invalid() && state.touched();
  }
}
```

`src/app/quotes/create-quote-form-signals/create-quote-form-signals.html`:

```html
<form [formRoot]="quoteForm">
  <div class="field">
    <label for="quote-text-signals">Quote text</label>
    <textarea
      id="quote-text-signals"
      #textInput
      rows="4"
      [formField]="quoteForm.text"
      [attr.aria-invalid]="showError(quoteForm.text) ? 'true' : null"
      [attr.aria-describedby]="showError(quoteForm.text) ? 'quote-text-signals-error' : null"
    ></textarea>
    <p id="quote-text-signals-error" class="error" role="alert">
      @if (showError(quoteForm.text)) {
        {{ quoteForm.text().errors()[0]?.message }}
      }
    </p>
  </div>

  <div class="field">
    <label for="quote-author-signals">Author</label>
    <input
      id="quote-author-signals"
      type="text"
      #authorInput
      [formField]="quoteForm.author"
      [attr.aria-invalid]="showError(quoteForm.author) ? 'true' : null"
      [attr.aria-describedby]="showError(quoteForm.author) ? 'quote-author-signals-error' : null"
    />
    <p id="quote-author-signals-error" class="error" role="alert">
      @if (showError(quoteForm.author)) {
        {{ quoteForm.author().errors()[0]?.message }}
      }
    </p>
  </div>

  <button type="submit" [disabled]="quoteForm().submitting()">
    {{ quoteForm().submitting() ? 'Saving…' : 'Save quote' }}
  </button>

  <p class="status" role="status">
    @if (quoteForm().submitting()) {
      Submitting…
    }
  </p>

  <p class="server-error" role="alert">
    @if (serverError(); as message) {
      {{ message }}
    }
  </p>

  <p class="success" role="status">
    @if (submittedQuote(); as quote) {
      Quote saved: "{{ quote }}"
    }
  </p>
</form>
```

### 3. The verification log

Real endpoint: `POST /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:364-376`) — matches the Academy's `/api/quotes` shorthand exactly. Request DTO: `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` — `public sealed record CreateQuoteRequest(string Text, string? Author = null);` — no validation attributes on either field, re-confirmed fresh from source (not trusted from Task 1's notes).

States exercised, real test names, real counts (`npx ng test --watch=false`, Vitest): `PRISTINE`, `DIRTY`, `TOUCHED`, `VALIDATORS FIRING`, `ERROR DISPLAY`, `CLEAN SUBMIT`, `SUBMITTED` (both the all-blank and text-valid-author-blank cases), `FAILED SUBMIT`, plus `CONTRACT` and `PARITY` tests — **56 passed (56)**, 6 spec files, split into **40 carried** (identical to day-14/task-1's in-place baseline, including the `RACE` test proving the Day 13 stale-response guard still works) + **16 new**. No named state was inexpressible; `pristine` has no direct Signal Forms equivalent and is asserted as `!dirty()` — see README.md.

Four genuine mistakes were made and fixed, in order (full detail with real error text in `verification-log.md` §4): (1) a real `TS2445` compile error from declaring `quoteForm` `protected`; (2) a wrong assumption that blurring fires on `focusout` — real evidence from `node_modules/@angular/forms/fesm2022/signals.mjs`: `host.listenToDom('blur', ...)`, not `focusout`; (3) a zoneless async-timing gap where `fixture.whenStable()` didn't wait for a hand-rolled Promise bridging `HttpClient`'s Observable into `submit()`'s required Promise return type, needing an explicit macrotask tick; (4) two `markAsTouched()` calls added defensively, then proven unnecessary by deleting them and re-running the full suite — `submit()` already marks attempted fields touched on an invalid submission. Mistake 2 is the headline one: it's specifically a case of the preview API behaving in an undocumented way the type signatures never say.

Two required mutation checks, both real: removing `required()` on `text` (the Signal Forms proof) broke 4/56 tests for real; reverted, back to 56/56. Reintroducing the classic dangling-`aria-describedby` bug broke 3/56 tests for real, including the accessibility resolution test; reverted, back to 56/56. Full output in `output/`.

What breaks if the contract changes: renaming `Text`/`Author` breaks the wire format silently in both forms identically; a genuine future length limit would be under-enforced by both equally; a shape change in the preview API itself (still `@experimental`) could break only the Signal Forms form's compile, not the reactive one.

### 4. The comparison

Where Signal Forms was simpler here: `submitting` comes free from `FieldState.submitting` (the reactive version needs a manual signal and three `.set()` calls); no `submitAttempted` flag was needed, since `submit()`'s `onInvalid` already leaves attempted fields touched (a genuine finding — see mistake 4 above); submission is declared once via `form(model, schema, { submission: {...} })` instead of assembled imperatively in a method.

Where it is still rough here: `submit()`'s `action` must return a `Promise`, but the shared `QuoteApi.createQuote()` returns an RxJS `Observable`, so a hand-rolled `new Promise((resolve, reject) => {...subscribe...})` bridge was needed — boilerplate the reactive version doesn't have; that same bridge caused a real zoneless test-timing gap (mistake 3); and which native event marks a field touched had to be found in the compiled `.mjs`, not the `.d.ts` files or JSDoc. No parity claim is made beyond `required()`, the only constraint this DTO actually has.

Full version: `comparison.md`.

### Interpretations

- Replicated from `day-14/task-1` at commit `32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba`; its `QuoteApi` service and `Quote`/`CreateQuoteRequest` model types are reused by the new form unchanged, not duplicated.
- Real endpoint and DTO: `day-3/task-3/QuotesApi/Program.cs:364-376` and `Quotes/QuoteRequests.cs`; matches `/api/quotes` exactly, no discrepancy from the Academy's shorthand.
- Signal Forms import path: `@angular/forms/signals`, version `21.2.21` (installed), verified via `node_modules/@angular/forms/package.json`'s `exports` map.
- Signal Forms is a preview in Angular 21: every symbol used carries `@experimental 21.0.0`/`21.2.0` in its own JSDoc; this limited nothing functionally here, but did mean the exact touched-marking event had to be found in compiled source, not documentation.
- No live authenticated call: the route needs a `quotes.write` JWT scope; every test (Signal Forms, reactive, and parity) uses `HttpTestingController`.
- Both forms kept deliberately, side by side on the same page, so the comparison is checkable rather than a claim about code elsewhere.
- Angular version unchanged from the carried app (`ng version` → `Angular CLI: 21.2.21`).
- Test runner: Vitest, via `@angular/build:unit-test` (the carried app's own runner) — no Karma added.

## What did you learn this session?

The real friction wasn't the Signal Forms API itself, it was that its actual runtime behavior (which event marks touched, whether onInvalid auto-touches fields) isn't fully written down anywhere — I had to grep the compiled JS and delete code to find out empirically. Reading a preview API's type file isn't the same as knowing how it behaves.

## What would break this?

Signal Forms here is `@experimental` throughout, so a future Angular minor could change `form()`'s options shape or `FormField`'s binding contract and this form would simply stop compiling, while the reactive form next to it wouldn't notice at all. And the hand-rolled Promise bridge in the submission action is exactly the kind of thing that could silently stop working if a future version changes how `submit()` awaits its action.
