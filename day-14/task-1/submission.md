## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-14/task-1/day-14/task-1

## Notes for mentor

This continues the Day 13 app, carried from `day-13/task-2` at commit `88efd46d4b0bc6f7ad13c47a5de0d3e2ac6a7bd9`, with the create-a-quote form added; Day 13's folder is unchanged (verified with `diff -rq day-13/task-2 day-14/task-1` and `git status`/`git diff --stat` against `day-13`). Post-submission, at Devansh's explicit request: a real `Author` field was added to the actual `day-3/task-3/QuotesApi` DTOs (previously frozen), optional server-side but made compulsory on the form; a local-only sign-in gate and dev-login helper were added for manual testing (not graded, not touched by any test); and every component was restyled to one consistent palette. Full accounting in `PROVENANCE.md`.

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

type FormField = 'text' | 'author';

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
  private readonly authorInput =
    viewChild.required<ElementRef<HTMLInputElement>>('authorInput');

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly submittedQuote = signal<string | null>(null);

  // Emits the created quote so the host page can add it to QuoteBrowser's
  // list without a second round trip. See app.ts.
  readonly quoteCreated = output<Quote>();

  private submitAttempted = false;

  // The real CreateQuoteRequest DTO (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs)
  // carries no validation attributes on either field -- both are optional,
  // nullable server-side. `text` being required here is a client-only UX
  // safety net (see README.md). `author` being required is the same kind of
  // directed, client-only decision -- Devansh asked for it to be compulsory
  // on the form; the server still accepts a request with no author at all,
  // so this is a deliberately stricter client rule, not a mirrored
  // constraint. Documented here so it stays checkable, not silently assumed.
  protected readonly form = new FormGroup({
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    author: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected get textControl() {
    return this.form.controls.text;
  }

  protected showError(field: FormField): boolean {
    const control = this.form.controls[field];
    return control.invalid && (control.touched || this.submitAttempted);
  }

  protected onSubmit(): void {
    if (this.submitting()) {
      return;
    }

    this.serverError.set(null);
    this.submitAttempted = true;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      // Focus the first invalid control in DOM order: text, then author.
      if (this.form.controls.text.invalid) {
        this.textInput().nativeElement.focus();
      } else {
        this.authorInput().nativeElement.focus();
      }
      return;
    }

    this.submitting.set(true);

    this.quoteApi
      .createQuote({
        text: this.textControl.value,
        author: this.form.controls.author.value.trim(),
      })
      .subscribe({
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
      [attr.aria-invalid]="showError('text') ? 'true' : null"
      [attr.aria-describedby]="showError('text') ? 'quote-text-error' : null"
    ></textarea>
    <p id="quote-text-error" class="error" role="alert">
      @if (showError('text')) {
        Quote text is required.
      }
    </p>
  </div>

  <div class="field">
    <label for="quote-author">Author</label>
    <input
      id="quote-author"
      type="text"
      formControlName="author"
      #authorInput
      [attr.aria-invalid]="showError('author') ? 'true' : null"
      [attr.aria-describedby]="showError('author') ? 'quote-author-error' : null"
    />
    <p id="quote-author-error" class="error" role="alert">
      @if (showError('author')) {
        Author is required.
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

Real endpoint: `POST /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:364-376`), gated by the `CanEditQuotes` policy. Request DTO: `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` — `public sealed record CreateQuoteRequest(string Text, string? Author = null);` — **no validation attributes on either field** (`grep -n "Required|MaxLength|StringLength|RegularExpression|Range(" ` across every file in `day-3/task-3/QuotesApi` returns zero matches). Response DTO: `day-3/task-3/QuotesApi/Quotes/Quote.cs`, matching `quote.ts`. `Author` was added to the real DTO on 2026-08-25 at Devansh's explicit request — see `PROVENANCE.md`, "Third commit," for the blast-radius check performed first (every project referencing this API directly was checked for any code that would break; none exists) and the full-repo regression proof (39 solutions, byte-identical counts before/after).

Validator mapping: `Validators.required` on both `text` and `author` mirrors **no** API constraint — there is none to mirror on either field, documented in-code as client-only rules (`author`'s was added at Devansh's explicit request to make it compulsory on the form even though the server still accepts a request with none).

States exercised, real test names, real counts (`npx ng test --watch=false`, Vitest): `EMPTY`, `INVALID`, `SUBMITTING`, `SERVER-ERROR`, plus `CONTRACT` and `AUTHOR` tests — **40 passed (40)**, 4 spec files, split into **15 carried** (identical count and test bodies to the in-place `day-13/task-2` baseline, including the `RACE` test proving the stale-response guard still works) + **25 new**. A11y: automated assertions (label-for match on both inputs, `aria-describedby` resolves to a real DOM element in both valid and invalid state, `aria-invalid` correct in both states, focus-to-first-invalid across two required fields in DOM order, no positive `tabindex`) — all passed, `output/final-test-run.txt`. Keyboard/screen-reader manual pass: **done, Devansh's own observation** — he ran VoiceOver over the form per the script in `output/manual-a11y-script.md` and reported it "is also making sense nothing idiotic," i.e. announcements made sense with nothing broken or confusing.

One genuine finding, not a bug: the real `CreateQuoteRequest` DTO originally had zero validation attributes and exactly one field, discovered by rereading the DTO before writing any code, which directly ruled out adding an invented `maxLength` or a second field at first. No correction of substance was needed while building the original form. Two required mutation checks confirm the tests mean something: removing `Validators.required` broke `11/33` tests at the time (real cascade via an unflushed `HttpTestingController` request); reverted, back to green. Reintroducing the classic dangling-`aria-describedby` bug broke `2/33` tests at the time, including `AssertionError: expected null to be truthy` on the DOM-resolution test — the a11y proof required by the task; reverted, back to green. Full output in `output/`. Devansh later asked for a real author field (added to the actual API, verified with a zero-regression full-repo sweep — `verification-log.md` §7), then asked for it to be made compulsory on the form: checked first whether that was safe server-side too, found it would have broken an already-passing test in `day-3/task-3/QuotesApi.Tests`, so it stayed client-only, same as `text` (`verification-log.md` §8). A later gating/redesign change broke two existing tests as a real, reproduced-not-assumed failure (`verification-log.md` §9), fixed the same session.

What breaks if the contract changes: renaming `Text` or `Author` on the DTO breaks the wire format silently (client keeps sending the old field names, nothing on either side notices at compile time); a genuine future `[MaxLength]` on either field would be under-enforced client-side until a SERVER-ERROR revealed it, since today's client enforces no length at all; if the server ever made `Author` genuinely required, the client's already-required validator would still be correct, but a mismatch in *how* required (e.g. a minimum length) would only surface as a SERVER-ERROR.

### Interpretations

- Carried from `day-13/task-2` (the later, fuller app — list-plus-detail with a stale-response guard, versus `task-1`'s list-only component); its `QuoteApi` and `Quote` model were extended and reused, not duplicated.
- API and DTO read from `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs` (request) and `Quote.cs` (response); route at `Program.cs:364-376`.
- No live authenticated call in the automated tests: the route needs a `quotes.write` JWT scope; tested entirely via `HttpTestingController`. A local-only manual verification path (dev credential, proxy, interceptor) was added separately at Devansh's request — see README.md, "Testing a real save locally" — and does not affect the tests.
- `ReactiveFormsModule` used, not the experimental Signal Forms (stabilizes only in Angular 22); `submitting`/`serverError` held as signals, consistent with the carried app's own style.
- `@angular/aria` not added: not required by the brief, and hand-wired ARIA is more directly testable.
- `aria-describedby` kept resolvable by always rendering its target `<p id="quote-text-error">`, only conditioning its text and the attribute binding, never the element itself.
- Angular version unchanged from the carried app (`ng version` → `Angular CLI: 21.2.21`, matching both Day 13 apps' `package.json`).
- Test runner: Vitest, via `@angular/build:unit-test` (the carried app's own runner) — no Karma added.
- `Author` was added to the real API post-submission, at Devansh's explicit direction, as a deliberate one-time exception to treating `day-3` as frozen — see `PROVENANCE.md` for the full blast-radius check and regression proof.
- `Author` was later made compulsory on the form (client-side `Validators.required`, same as `text`) after confirming server-side would break an existing passing test; the server DTO itself stays optional.
- A local-only sign-in gate, dev-login helper, and unified color palette were added post-submission for Devansh's manual testing; none of it is exercised by any test or part of the graded deliverable.

## What did you learn this session?

Carrying the app forward instead of rebuilding it meant the real risk was quietly breaking the stale-response guard while wiring the new list update, not the form itself. When the contract needed a real, requested change afterward, checking every dependent project first — and checking what making a field required server-side would break — mattered more than the edit itself.

## What would break this?

If the server ever added a real `[MaxLength]` to either field, this client would keep accepting longer input until a SERVER-ERROR revealed the mismatch. And extending a shared API file like `day-3/task-3/QuotesApi` without first checking every project that references it directly could silently break other days' already-graded tests — which is exactly what would have happened had `Author` been made required on the server without checking first.
