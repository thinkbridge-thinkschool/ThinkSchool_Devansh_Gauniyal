/**
 * Single source of truth for the quotes feature's state -- the list QuoteBrowser
 * renders, the selection QuoteDetailPage renders, and how a newly created quote lands
 * back in the list. Consolidates state that used to be duplicated: QuoteBrowser and
 * QuoteDetailPage each held their own copy of this state (their own `Quote[]`/`Quote |
 * null`, their own loading/error signals) and each independently called QuoteApi and
 * guarded against stale responses. See PROVENANCE.md / README.md for the "before"
 * picture this replaces.
 *
 * Routes this store calls (day-3/task-3/QuotesApi/Program.cs):
 *   GET /api/quotes (Program.cs:361-362, no auth) -- loadQuotes() calls it directly;
 *     selectQuote() re-calls the SAME endpoint and resolves the requested id
 *     client-side, since the API has no per-item GET route (confirmed by grepping
 *     Program.cs for `MapGet` -- only PUT/DELETE `/api/quotes/{id:int}` exist, both
 *     auth-gated mutations, not reads).
 *   POST /api/quotes is NOT called from here. Creating a quote stays exactly where it
 *     already was -- CreateQuoteForm / CreateQuoteFormSignals still call
 *     QuoteApi.createQuote() directly -- only the resulting quote's effect on THIS
 *     store's list (addQuote()) is this store's concern, per the brief's "the create
 *     flow's effect on the list".
 *
 * Concurrency guard: both loadQuotes() and selectQuote() use a private, monotonically
 * increasing request token -- one counter per operation. Each call captures the
 * counter's new value as its own token before issuing the HTTP request; when a
 * response arrives, it is applied only if its captured token still equals the
 * counter's CURRENT value, i.e. only if no newer call to the same method has started
 * since. A response whose token has been superseded is silently discarded. This is a
 * generalisation of the id-comparison guard quote-detail-page.ts used before this move
 * (which compared the requested id against the CURRENT id): a token also correctly
 * supersedes a second call for the very same id, which an id-comparison alone could
 * not distinguish. The identical pattern now also covers loadQuotes(), which had no
 * guard at all before this move.
 */
import { Injectable, computed, inject, signal } from '@angular/core';
import { AppHttpError } from '../http/app-http-error';
import { QuoteApi } from './quote-api';
import { Quote } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly api = inject(QuoteApi);

  private readonly _quotes = signal<Quote[] | null>(null);
  private readonly _listLoading = signal(true);
  private readonly _listError = signal<string | null>(null);

  private readonly _selectedQuote = signal<Quote | null>(null);
  private readonly _detailLoading = signal(false);
  private readonly _detailError = signal<string | null>(null);

  // Read-only views: components can read these but cannot call .set()/.update() on
  // them -- asReadonly() returns a WritableSignal-free Signal, so there is no
  // mutating method on the type components are handed at all, not merely a
  // convention against using one.
  readonly quotes = this._quotes.asReadonly();
  readonly listLoading = this._listLoading.asReadonly();
  readonly listError = this._listError.asReadonly();

  readonly selectedQuote = this._selectedQuote.asReadonly();
  readonly detailLoading = this._detailLoading.asReadonly();
  readonly detailError = this._detailError.asReadonly();

  // Derived from `quotes` -- never assigned directly, anywhere in this file, so they
  // can never drift out of sync with the collection they are computed from.
  readonly quoteCount = computed(() => this._quotes()?.length ?? 0);
  readonly isEmpty = computed(() => this._quotes() !== null && this._quotes()!.length === 0);

  private listRequestToken = 0;
  private detailRequestToken = 0;

  loadQuotes(): void {
    const token = ++this.listRequestToken;
    this._listLoading.set(true);
    this._listError.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        if (token !== this.listRequestToken) {
          return; // superseded by a newer loadQuotes() call -- discard, do not apply
        }
        this._quotes.set(quotes);
        this._listLoading.set(false);
      },
      error: (err: unknown) => {
        if (token !== this.listRequestToken) {
          return;
        }
        this._listError.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quotes.');
        this._listLoading.set(false);
      },
    });
  }

  selectQuote(id: number): void {
    const token = ++this.detailRequestToken;
    this._selectedQuote.set(null);
    this._detailError.set(null);
    this._detailLoading.set(true);
    this.api.getQuoteDetail(id).subscribe({
      next: (found) => {
        if (token !== this.detailRequestToken) {
          return; // superseded by a newer selectQuote() call -- discard, do not apply
        }
        this._selectedQuote.set(found ?? null);
        this._detailLoading.set(false);
      },
      error: (err: unknown) => {
        if (token !== this.detailRequestToken) {
          return;
        }
        this._detailError.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quote detail.');
        this._detailLoading.set(false);
      },
    });
  }

  // Clears the selection without issuing a request -- used when a route param is
  // absent or malformed (see quote-detail-page.ts) so a stale selection from a
  // previous valid id does not linger. Also bumps the token, so a detail request
  // already in flight from a prior selectQuote() call is superseded and its response
  // (whenever it lands) is discarded rather than repopulating the just-cleared state.
  clearSelection(): void {
    this.detailRequestToken++;
    this._selectedQuote.set(null);
    this._detailLoading.set(false);
    this._detailError.set(null);
  }

  // The create flow's effect on the list (see CreateQuoteForm / CreateQuoteFormSignals,
  // both of which call this after a successful POST): appends an already-created quote
  // to the collection. Always REPLACES the array (spreads into a new one) rather than
  // mutating the existing array in place -- a signal only notifies subscribers on
  // reassignment; push()ing into an array a signal already holds changes the array's
  // contents without changing which array the signal holds, so nothing would react.
  addQuote(quote: Quote): void {
    const current = this._quotes() ?? [];
    if (current.some((existing) => existing.id === quote.id)) {
      return;
    }
    this._quotes.set([quote, ...current]);
  }
}
