/**
 * Routed shell for '/' (see ../app.routes.ts). Everything App used to render directly
 * when authenticated now lives here instead: the demo panel, both create-a-quote forms,
 * the quote list, and a nested <router-outlet /> for 'quotes' / 'quotes/:id'. authGuard
 * on the parent route keeps an unauthenticated visitor from ever reaching this component.
 */
import { Component, inject, signal } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { DevLogin } from '../auth/dev-login/dev-login';
import { HttpDemoPanel } from '../demo/http-demo-panel/http-demo-panel';
import { CreateQuoteForm } from '../quotes/create-quote-form/create-quote-form';
import { CreateQuoteFormSignals } from '../quotes/create-quote-form-signals/create-quote-form-signals';
import { QuoteBrowser } from '../quotes/quote-browser/quote-browser';
import type { Quote } from '../quotes/quote';

@Component({
  selector: 'app-home-page',
  imports: [QuoteBrowser, CreateQuoteForm, CreateQuoteFormSignals, DevLogin, HttpDemoPanel, RouterOutlet],
  templateUrl: './home-page.html',
  styleUrl: './home-page.css',
})
export class HomePage {
  private readonly router = inject(Router);

  protected readonly justCreated = signal<Quote | null>(null);

  protected onLoggedOut(): void {
    this.router.navigateByUrl('/login');
  }
}
