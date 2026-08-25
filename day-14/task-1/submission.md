## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-14/task-1/day-14/task-1

## Notes for mentor

This continues the Day 13 app, carried from `day-13/task-2` at commit `88efd46d4b0bc6f7ad13c47a5de0d3e2ac6a7bd9`, with the create-a-quote form added; Day 13's folder is unchanged (verified with `diff -rq day-13/task-2 day-14/task-1` and `git status`/`git diff --stat` against `day-13`).

### 1. The brief

Continue the Angular 21 app I built on Day 13 — the quotes list-plus-detail component. Copy that app into the Day 14 folder untouched, then ADD a reactive create-a-quote form to it as a new feature. The existing list and detail must keep working, and Day 13's folder must not be modified.

The form goes against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Read the POST endpoint's route, its request DTO and every validation attribute on that DTO. The form shape and every validator must come from those actual fields and constraints. Do not invent a field, do not omit a required one, and do not pick a length limit that differs from the API's. Reuse the model types and service the Day 13 app already established rather than writing a parallel copy.

Requirements:
- a reactive form using Angular's ReactiveFormsModule, standalone component, no NgModule anywhere
- one validator per real API constraint, matching its limits exactly — required, maximum length, and anything else the DTO enforces
- error messages that appear only after the field is touched or the form is submitted, never on an untouched empty form
- full accessibility wiring:
    * every input has a <label for="..."> bound to that input's id
    * aria-invalid reflects the field's real validity state
    * aria-describedby points at the id of the element containing that field's error message, and that element must exist in the DOM whenever the attribute references it
    * the whole form is operable by keyboard alone, with a visible focus indicator
    * on submit with errors, focus moves programmatically to the first invalid field
- four states handled distinctly: empty, invalid, submitting (control disabled or a busy indicator), and server-error (the API rejected it)
- a server error must surface to the user and be announced to assistive technology, never swallowed
- inject() for every dependency, never constructor injection; the model fully typed, no `any`
- a successful submit should be reflected in the existing list where that is natural, without breaking the Day 13 stale-response guard

Do not add form libraries, UI component libraries, validation libraries, or anything beyond what was already in the Day 13 app. Zoneless is the Angular 21 default — do not re-add Zone.js.

### 2. The agent's output

`src/app/quotes/create-quote-form/create-quote-form.ts`:

```ts
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { QuoteApi } from '../quote-api';
import type { Quote } from '../quote';

@Component({
  selector: 'app-create-quote-form',
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote-form.html',
  styleUrl: './create-quote-form.css',
})
export class CreateQuoteForm {
  private readonly quoteApi = inject(QuoteApi);
  private readonly textInput =
    viewChild.required<ElementRef<HTMLTextAreaElement>>('textInput');

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly submittedQuote = signal<string | null>(null);

  // Emits the created quote so the host page can add it to QuoteBrowser's
  // list without a second round trip. See app.ts.
  readonly quoteCreated = output<Quote>();

  private submitAttempted = false;

  // The real CreateQuoteRequest DTO (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs)
  // carries no validation attributes at all, so Validators.required below is NOT
  // mirroring a server-side constraint -- there isn't one to mirror. It is a
  // client-only safety net against submitting a blank quote. See README.md,
  // "Why there is exactly one field and one validator".
  protected readonly form = new FormGroup({
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected get textControl() {
    return this.form.controls.text;
  }

  protected showError(): boolean {
    return this.textControl.invalid && (this.textControl.touched || this.submitAttempted);
  }

  protected onSubmit(): void {
    if (this.submitting()) {
      return;
    }

    this.serverError.set(null);
    this.submitAttempted = true;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.textInput().nativeElement.focus();
      return;
    }

    this.submitting.set(true);

    this.quoteApi.createQuote({ text: this.textControl.value }).subscribe({
      next: (quote) => {
        this.submitting.set(false);
        this.submittedQuote.set(quote.text);
        this.form.reset();
        this.submitAttempted = false;
        this.quoteCreated.emit(quote);
      },
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.serverError.set(
          error.status === 401 || error.status === 403
            ? 'You are not authorized to create quotes.'
            : 'The quote could not be saved. Please try again.',
        );
      },
    });
  }
}
```

`src/app/quotes/create-quote-form/create-quote-form.html`:

```html
<form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
  <div class="field">
    <label for="quote-text">Quote text</label>
    <textarea
      id="quote-text"
      formControlName="text"
      #textInput
      rows="4"
      [attr.aria-invalid]="showError() ? 'true' : null"
      [attr.aria-describedby]="showError() ? 'quote-text-error' : null"
    ></textarea>
    <p id="quote-text-error" class="error" role="alert">
      @if (showError()) {
        Quote text is required.
      }
    </p>
  </div>

  <button type="submit" [disabled]="submitting()">
    {{ submitting() ? 'Saving…' : 'Save quote' }}
  </button>

  <p class="status" role="status">
    @if (submitting()) {
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

The service method added, extending `QuoteApi` (`src/app/quotes/quote-api.ts`) rather than duplicating it:

```ts
  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(QUOTES_ENDPOINT, request);
  }
```

### 3. The verification log

Real endpoint: `POST /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:364-376`), gated by the `CanEditQuotes` policy. Request DTO: `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` — `public sealed record CreateQuoteRequest(string Text);` — **one field, no validation attributes at all** (`grep -n "Required|MaxLength|StringLength|RegularExpression|Range(" ` across every file in `day-3/task-3/QuotesApi` returns zero matches). Response DTO: `day-3/task-3/QuotesApi/Quotes/Quote.cs`, already modeled by the carried `quote.ts`.

Validator mapping: `Validators.required` on `text` mirrors **no** API constraint — there is none to mirror — documented in-code as a client-only UX safety net. No `maxLength` validator was added, since inventing one would misrepresent the API. No second field was added, since the DTO has only one.

States exercised, real test names, real counts (`npx ng test --watch=false`, Vitest): `EMPTY`, `INVALID`, `SUBMITTING`, `SERVER-ERROR`, plus `CONTRACT` tests — **33 passed (33)**, 4 spec files, split into **15 carried** (identical count and test bodies to the in-place `day-13/task-2` baseline, including the `RACE` test proving the stale-response guard still works) + **18 new**. A11y: automated assertions (label-for match, `aria-describedby` resolves to a real DOM element in both valid and invalid state, `aria-invalid` correct in both states, focus moves to the field after a failed submit, no positive `tabindex`) — all passed, `output/final-test-run.txt`. Keyboard/screen-reader manual pass: **PENDING** — not yet performed by Devansh; script in `output/manual-a11y-script.md`.

One genuine finding, not a bug: the real `CreateQuoteRequest` DTO has zero validation attributes, discovered by rereading the DTO before writing any code, which directly ruled out adding an invented `maxLength` or a second field. No correction of substance was needed while building — the carried copy's 15 tests passed unchanged immediately after the copy (Step 4B, before any new code), and the full 33-test suite passed on the first real run after adding the form. Two required mutation checks confirm the tests mean something: removing `Validators.required` broke `11/33` tests (real cascade via an unflushed `HttpTestingController` request); reverted, back to `33/33`. Reintroducing the classic dangling-`aria-describedby` bug (attribute always present, error `<p>` element itself `@if`-removed) broke `2/33` tests, including `AssertionError: expected null to be truthy` on the DOM-resolution test — the a11y proof required by the task; reverted, back to `33/33`. Full output in `output/`.

What breaks if the contract changes: renaming `Text` on the DTO breaks the wire format silently (client keeps sending `text`, nothing on either side notices at compile time); a genuine future `[MaxLength]` on `Text` would be under-enforced client-side until a SERVER-ERROR revealed it, since today's client enforces no length at all; a new required field would need a new control, label, and `aria-describedby` id that don't exist today.

### Interpretations

- Carried from `day-13/task-2` (the later, fuller app — list-plus-detail with a stale-response guard, versus `task-1`'s list-only component); its `QuoteApi` and `Quote` model were extended and reused, not duplicated.
- API and DTO read from `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` (request) and `Quote.cs` (response); route at `Program.cs:364-376`.
- No live authenticated call: the route needs a `quotes.write` JWT scope; tested entirely via `HttpTestingController` instead.
- `ReactiveFormsModule` used, not the experimental Signal Forms (stabilizes only in Angular 22); `submitting`/`serverError` held as signals, consistent with the carried app's own style.
- `@angular/aria` not added: not required by the brief, and hand-wired ARIA is more directly testable.
- `aria-describedby` kept resolvable by always rendering its target `<p id="quote-text-error">`, only conditioning its text and the attribute binding, never the element itself.
- Angular version unchanged from the carried app (`ng version` → `Angular CLI: 21.2.21`, matching both Day 13 apps' `package.json`).
- Test runner: Vitest, via `@angular/build:unit-test` (the carried app's own runner) — no Karma added.

## What did you learn this session?

Carrying the app forward instead of rebuilding it meant the real risk wasn't the form itself, it was quietly breaking the stale-response guard while wiring the new list update. Keeping `selectQuote()` byte-identical and only appending new tests, never editing old ones, made that risk visible in the diff instead of hidden in it.

## What would break this?

If the server ever added a real `[MaxLength]` to `Text`, this client would keep accepting longer input until a SERVER-ERROR revealed the mismatch. And if `justCreated`'s effect were ever changed to replace `listData` instead of prepending to it, a newly created quote would silently wipe out the rest of the list instead of joining it.
