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
