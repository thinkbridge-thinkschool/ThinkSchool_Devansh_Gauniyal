import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from '../quotes-store';

@Component({
  selector: 'app-quote-browser',
  imports: [RouterLink],
  templateUrl: './quote-browser.html',
  styleUrl: './quote-browser.css',
})
export class QuoteBrowser implements OnInit {
  private readonly store = inject(QuotesStore);

  protected readonly quotes = this.store.quotes;
  protected readonly listLoading = this.store.listLoading;
  protected readonly listError = this.store.listError;
  protected readonly isEmpty = this.store.isEmpty;

  ngOnInit(): void {
    this.store.loadQuotes();
  }
}
