/**
 * Route-driven detail page for the lazy-loaded 'quotes' / 'quotes/:id' routes -- see
 * ../../app.routes.ts. This is deliberately separate from QuoteBrowser's existing
 * inline detail pane (quote-browser.ts), which keeps working completely unchanged.
 * This component exists only to be reached by router navigation, so it can prove the
 * lazy chunk, the guard, the route-param edges and the view transition this task asks
 * for, without touching the carried composition-based feature at all.
 *
 * The real API (day-3/task-3/QuotesApi, see quote-api.ts's own header comment) has no
 * per-item GET route -- only GET /api/quotes (list). getQuoteDetail() below re-calls
 * that one real endpoint and resolves the requested id client-side, exactly as
 * QuoteBrowser already does; the "detail" fetch is still a genuine, independently-timed
 * HTTP round trip.
 */
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppHttpError } from '../../http/app-http-error';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

export type QuoteDetailPageParamProblem = 'missing' | 'malformed';

// The real id field is Quote.Id, a non-negative auto-incrementing C# `int`
// (day-3/task-3/QuotesApi/Quotes/Quote.cs; InMemoryQuoteRepository.cs seeds
// `_nextId` and never issues a negative or non-integer id) -- so a route param is
// "malformed" here whenever it is not a plain non-negative integer string.
const NON_NEGATIVE_INTEGER = /^\d+$/;

@Component({
  selector: 'app-quote-detail-page',
  imports: [RouterLink],
  templateUrl: './quote-detail-page.html',
  styleUrl: './quote-detail-page.css',
})
export class QuoteDetailPage {
  private readonly api = inject(QuoteApi);

  // Route param bound by withComponentInputBinding() (see app.config.ts): undefined on
  // the paramless 'quotes' route, a raw (possibly non-numeric) string on 'quotes/:id'.
  readonly id = input<string>();

  protected readonly paramProblem = computed<QuoteDetailPageParamProblem | null>(() => {
    const raw = this.id();
    if (raw === undefined || raw.trim() === '') {
      return 'missing';
    }
    return NON_NEGATIVE_INTEGER.test(raw) ? null : 'malformed';
  });

  private readonly numericId = computed<number | null>(() => {
    const raw = this.id();
    return raw !== undefined && this.paramProblem() === null ? Number(raw) : null;
  });

  protected readonly loading = signal(false);
  protected readonly quote = signal<Quote | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  // Stale-response guard, the same pattern QuoteBrowser.selectQuote() already uses: `id`
  // is captured at request time by this closure, and a later id change may move
  // numericId() on before this response lands, in which case it must be discarded.
  private readonly loadOnIdChange = effect(() => {
    const id = this.numericId();
    this.quote.set(null);
    this.errorMessage.set(null);
    if (id === null) {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.api.getQuoteDetail(id).subscribe({
      next: (found) => {
        if (this.numericId() !== id) {
          return;
        }
        this.quote.set(found ?? null);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        if (this.numericId() !== id) {
          return;
        }
        this.errorMessage.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quote detail.');
        this.loading.set(false);
      },
    });
  });
}
