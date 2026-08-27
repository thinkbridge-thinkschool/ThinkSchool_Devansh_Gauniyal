import { Component, OnInit, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppHttpError } from '../../http/app-http-error';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

@Component({
  selector: 'app-quote-browser',
  imports: [RouterLink],
  templateUrl: './quote-browser.html',
  styleUrl: './quote-browser.css',
})
export class QuoteBrowser implements OnInit {
  private readonly api = inject(QuoteApi);

  // Set by the host (see home-page.ts) when CreateQuoteForm's (quoteCreated) output
  // fires, so a newly saved quote is reflected here without a second round trip.
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
}
