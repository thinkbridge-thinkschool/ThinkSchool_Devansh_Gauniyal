/**
 * Route-driven detail page for the lazy-loaded 'quotes' / 'quotes/:id' routes -- see
 * ../../app.routes.ts. Route-param parsing (id, paramProblem, numericId) stays local to
 * this component, since it is a routing concern specific to this page, not shared
 * feature state. The actual data -- the selected quote, its loading flag, its error --
 * now lives in QuotesStore (../quotes-store.ts) instead of this component's own
 * signals, so QuoteBrowser and this page share one source of truth instead of each
 * independently calling QuoteApi and independently guarding against stale responses.
 *
 * The real API (day-3/task-3/QuotesApi, see quotes-store.ts's own header comment) has
 * no per-item GET route -- store.selectQuote() re-calls the one real list endpoint and
 * resolves the requested id client-side, exactly as before this move.
 */
import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from '../quotes-store';

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
  private readonly store = inject(QuotesStore);

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

  // Store-owned state, read directly -- see quotes-store.ts. Not reassigned here.
  protected readonly loading = this.store.detailLoading;
  protected readonly quote = this.store.selectedQuote;
  protected readonly errorMessage = this.store.detailError;

  // Drives the store's selection from the route param. store.selectQuote() carries its
  // own stale-response guard (a request token, see quotes-store.ts); store.clearSelection()
  // handles the missing/malformed cases so a stale selection from a previous valid id
  // does not linger and any in-flight request for it is superseded.
  private readonly loadOnIdChange = effect(() => {
    const id = this.numericId();
    if (id === null) {
      this.store.clearSelection();
      return;
    }
    this.store.selectQuote(id);
  });
}
