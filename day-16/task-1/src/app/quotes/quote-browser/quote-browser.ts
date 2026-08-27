import { Component, OnInit, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppHttpError } from '../../http/app-http-error';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

@Component({
  selector: 'app-quote-browser',
  // RouterLink added for the "Open detail page" link per list item -- see
  // quote-browser.html and app.routes.ts -- so the routed, guarded, lazily-loaded
  // quote-detail-page.ts is reachable from the existing list. Nothing about the
  // existing inline select/detail behaviour below is touched.
  imports: [RouterLink],
  templateUrl: './quote-browser.html',
  styleUrl: './quote-browser.css',
})
export class QuoteBrowser implements OnInit {
  private readonly api = inject(QuoteApi);

  // Set by the host (see app.ts) when CreateQuoteForm's (quoteCreated) output
  // fires, so a newly saved quote is reflected here without a second round
  // trip. Added for Day 14; does not touch selectQuote()'s stale-response
  // guard below.
  readonly justCreated = input<Quote | null>(null);

  public readonly listData = signal<Quote[] | null>(null);
  public readonly listLoading = signal(true);
  public readonly listError = signal<string | null>(null);

  private readonly syncJustCreated = effect(() => {
    const quote = this.justCreated();
    if (quote === null) {
      return;
    }
    this.listData.update((current) => {
      const existing = current ?? [];
      return existing.some((q) => q.id === quote.id) ? existing : [quote, ...existing];
    });
  });

  public readonly selectedId = signal<number | null>(null);
  public readonly detailData = signal<Quote | null>(null);
  public readonly detailLoading = signal(false);
  public readonly detailError = signal<string | null>(null);

  ngOnInit(): void {
    this.loadList();
  }

  private loadList(): void {
    this.listLoading.set(true);
    this.listError.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        this.listData.set(quotes);
        this.listLoading.set(false);
      },
      error: (err: unknown) => {
        // err is an AppHttpError with a friendly message when the app's real
        // interceptor chain is wired (see app.config.ts); a spec that provides its own
        // provideHttpClient() without those interceptors (every carried spec does)
        // gets a raw HttpErrorResponse here instead, so this falls back to the
        // existing static message rather than assuming the typed shape.
        this.listError.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quotes.');
        this.listLoading.set(false);
      },
    });
  }

  public selectQuote(id: number): void {
    this.selectedId.set(id);
    this.detailLoading.set(true);
    this.detailError.set(null);
    this.detailData.set(null);

    // Stale-response guard: `id` is captured at request time by this closure. By the
    // time this callback runs, a later call to selectQuote() may already have moved
    // selectedId() on. If so, this response belongs to a selection that is no longer
    // current and must be discarded rather than applied.
    this.api.getQuoteDetail(id).subscribe({
      next: (quote) => {
        if (this.selectedId() !== id) {
          return;
        }
        this.detailData.set(quote ?? null);
        this.detailLoading.set(false);
      },
      error: (err: unknown) => {
        if (this.selectedId() !== id) {
          return;
        }
        this.detailError.set(
          err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quote detail.',
        );
        this.detailLoading.set(false);
      },
    });
  }
}
