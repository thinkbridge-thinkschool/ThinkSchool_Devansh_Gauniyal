import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnInit, inject, input, output, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { QuoteApi } from '../quote-api';
import { QuotesStore } from '../quotes-store';
import type { Quote } from '../quote';

type FormField = 'text' | 'author';

@Component({
  selector: 'app-edit-quote-form',
  imports: [ReactiveFormsModule],
  templateUrl: './edit-quote-form.html',
  styleUrl: './edit-quote-form.css',
})
export class EditQuoteForm implements OnInit {
  private readonly quoteApi = inject(QuoteApi);
  private readonly store = inject(QuotesStore);
  private readonly textInput =
    viewChild.required<ElementRef<HTMLTextAreaElement>>('textInput');
  private readonly authorInput =
    viewChild.required<ElementRef<HTMLInputElement>>('authorInput');

  // Read once at construction to seed the form -- this component is (re)created fresh
  // each time QuoteDetailPage enters edit mode (see quote-detail-page.html's @if), so
  // there is no later value of `quote` to stay in sync with here.
  readonly quote = input.required<Quote>();

  readonly saved = output<Quote>();
  readonly cancelled = output<void>();

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);

  private submitAttempted = false;

  // Built in ngOnInit, not a field initializer or the constructor -- the Angular
  // compiler statically rejects reading a required input() before that point (NG8118),
  // since input binding isn't guaranteed to have happened yet.
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

  ngOnInit(): void {
    const quote = this.quote();
    this.form.setValue({ text: quote.text, author: quote.author ?? '' });
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
      if (this.form.controls.text.invalid) {
        this.textInput().nativeElement.focus();
      } else {
        this.authorInput().nativeElement.focus();
      }
      return;
    }

    this.submitting.set(true);

    this.quoteApi
      .updateQuote(this.quote().id, {
        text: this.form.controls.text.value,
        author: this.form.controls.author.value.trim(),
      })
      .subscribe({
        next: (quote) => {
          this.submitting.set(false);
          this.store.replaceQuote(quote);
          this.saved.emit(quote);
        },
        error: (error: HttpErrorResponse) => {
          this.submitting.set(false);
          this.serverError.set(
            error.status === 401 || error.status === 403
              ? 'You are not authorized to edit quotes.'
              : error.status === 409
                ? 'You already have a quote with this exact text.'
                : 'The quote could not be saved. Please try again.',
          );
        },
      });
  }

  protected onCancel(): void {
    this.cancelled.emit();
  }
}
