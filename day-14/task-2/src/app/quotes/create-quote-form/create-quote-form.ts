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
