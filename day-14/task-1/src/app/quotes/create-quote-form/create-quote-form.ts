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
