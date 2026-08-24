import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

export type QuoteViewMode = 'list' | 'compact' | 'ids-only';

@Component({
  selector: 'app-quote-list',
  imports: [],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
})
export class QuoteList implements OnInit {
  private readonly api = inject(QuoteApi);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly filterText = signal('');
  protected readonly viewMode = signal<QuoteViewMode>('list');
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly filteredQuotes = computed(() => {
    const term = this.filterText().trim().toLowerCase();
    const all = this.quotes();
    return term === '' ? all : all.filter((quote) => quote.text.toLowerCase().includes(term));
  });

  protected readonly filteredCount = computed(() => this.filteredQuotes().length);

  ngOnInit(): void {
    this.loadQuotes();
  }

  protected setFilterText(value: string): void {
    this.filterText.set(value);
  }

  protected setViewMode(mode: QuoteViewMode): void {
    this.viewMode.set(mode);
  }

  private loadQuotes(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load quotes.');
        this.loading.set(false);
      },
    });
  }
}
