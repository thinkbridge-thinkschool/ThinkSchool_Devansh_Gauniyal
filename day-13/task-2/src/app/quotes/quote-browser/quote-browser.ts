import { Component, OnInit, inject, signal } from '@angular/core';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

@Component({
  selector: 'app-quote-browser',
  imports: [],
  templateUrl: './quote-browser.html',
  styleUrl: './quote-browser.css',
})
export class QuoteBrowser implements OnInit {
  private readonly api = inject(QuoteApi);

  public readonly listData = signal<Quote[] | null>(null);
  public readonly listLoading = signal(true);
  public readonly listError = signal<string | null>(null);

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
      error: () => {
        this.listError.set('Failed to load quotes.');
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
      error: () => {
        if (this.selectedId() !== id) {
          return;
        }
        this.detailError.set('Failed to load quote detail.');
        this.detailLoading.set(false);
      },
    });
  }
}
