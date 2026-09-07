import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { QuoteApi } from '../quote-api';
import { QuotesStore } from '../quotes-store';
import type { Quote } from '../quote';

type FormField = 'text' | 'author' | 'description';

@Component({
  selector: 'app-create-quote-form',
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote-form.html',
  styleUrl: './create-quote-form.css',
})
export class CreateQuoteForm {
  private readonly quoteApi = inject(QuoteApi);
  private readonly store = inject(QuotesStore);
  private readonly textInput =
    viewChild.required<ElementRef<HTMLTextAreaElement>>('textInput');
  private readonly authorInput =
    viewChild.required<ElementRef<HTMLInputElement>>('authorInput');

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly submittedQuote = signal<string | null>(null);

  // Emits the created quote on success -- kept for any listener that wants to know a
  // quote was just created. The quotes list itself no longer depends on this: on
  // success this component also calls QuotesStore.addQuote() directly (see below),
  // which is what QuoteBrowser's list actually reads from now (see quotes-store.ts).
  readonly quoteCreated = output<Quote>();

  private submitAttempted = false;

  // The real CreateQuoteRequest DTO (api/QuotesApi/Quotes/QuoteRequests.cs)
  // carries no validation attributes on any field -- all three are optional,
  // nullable server-side. `text` being required here is a client-only UX
  // safety net (see README.md). `author` being required is the same kind of
  // directed, client-only decision -- Devansh asked for it to be compulsory
  // on the form; the server still accepts a request with no author at all,
  // so this is a deliberately stricter client rule, not a mirrored
  // constraint. `description` is left optional, matching the server's own
  // lack of a constraint on it -- there was no direction to make it
  // required, unlike author.
  protected readonly form = new FormGroup({
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    author: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    description: new FormControl('', {
      nonNullable: true,
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

    const description = this.form.controls.description.value.trim();

    this.quoteApi
      .createQuote({
        text: this.textControl.value,
        author: this.form.controls.author.value.trim(),
        // Omitted entirely, not sent as `description: undefined`, when blank --
        // an object literal with an explicit `undefined` value still has that
        // key under Object.keys(), which is what the request-shape contract
        // tests check (they run against HttpTestingController's captured
        // pre-serialization object, not JSON.stringify's output).
        ...(description ? { description } : {}),
      })
      .subscribe({
        next: (quote) => {
          this.submitting.set(false);
          this.submittedQuote.set(quote.text);
          this.form.reset();
          this.submitAttempted = false;
          this.store.addQuote(quote);
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
