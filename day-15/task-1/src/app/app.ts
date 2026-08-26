import { Component, signal } from '@angular/core';
import { DevLogin } from './auth/dev-login/dev-login';
import { HttpDemoPanel } from './demo/http-demo-panel/http-demo-panel';
import { CreateQuoteForm } from './quotes/create-quote-form/create-quote-form';
import { CreateQuoteFormSignals } from './quotes/create-quote-form-signals/create-quote-form-signals';
import { QuoteBrowser } from './quotes/quote-browser/quote-browser';
import type { Quote } from './quotes/quote';

@Component({
  selector: 'app-root',
  imports: [QuoteBrowser, CreateQuoteForm, CreateQuoteFormSignals, DevLogin, HttpDemoPanel],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly justCreated = signal<Quote | null>(null);

  // Gates the whole page behind DevLogin -- local dev convenience only, see
  // dev-login.ts. Initialized from the same localStorage key the auth
  // interceptor reads, so a page refresh with a still-valid token stays
  // signed in.
  protected readonly isAuthenticated = signal(!!localStorage.getItem('devAuthToken'));
}
