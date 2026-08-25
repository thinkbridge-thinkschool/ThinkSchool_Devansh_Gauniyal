import { Component, signal } from '@angular/core';
import { DevLogin } from './auth/dev-login/dev-login';
import { CreateQuoteForm } from './quotes/create-quote-form/create-quote-form';
import { QuoteBrowser } from './quotes/quote-browser/quote-browser';
import type { Quote } from './quotes/quote';

@Component({
  selector: 'app-root',
  imports: [QuoteBrowser, CreateQuoteForm, DevLogin],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly justCreated = signal<Quote | null>(null);
}
