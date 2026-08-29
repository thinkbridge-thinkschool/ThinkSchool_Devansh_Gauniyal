/**
 * Routed shell for '/' (see ../app.routes.ts). Everything App used to render directly
 * when authenticated now lives here instead: the demo panel, both create-a-quote forms,
 * the quote list, and a nested <router-outlet /> for 'quotes' / 'quotes/:id'. authGuard
 * on the parent route keeps an unauthenticated visitor from ever reaching this component.
 *
 * The `justCreated` bridge signal this component used to hold (passed from
 * CreateQuoteForm's (quoteCreated) output down into QuoteBrowser's `justCreated` input)
 * is gone: both forms now call QuotesStore.addQuote() directly on success (see
 * create-quote-form.ts / create-quote-form-signals.ts), and QuoteBrowser reads the
 * resulting list straight from QuotesStore -- there is no longer a copy of this state
 * for HomePage to shuttle between them.
 */
import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { DevLogin } from '../auth/dev-login/dev-login';
import { HttpDemoPanel } from '../demo/http-demo-panel/http-demo-panel';
import { CreateQuoteForm } from '../quotes/create-quote-form/create-quote-form';
import { CreateQuoteFormSignals } from '../quotes/create-quote-form-signals/create-quote-form-signals';
import { QuoteBrowser } from '../quotes/quote-browser/quote-browser';

@Component({
  selector: 'app-home-page',
  imports: [QuoteBrowser, CreateQuoteForm, CreateQuoteFormSignals, DevLogin, HttpDemoPanel, RouterOutlet],
  templateUrl: './home-page.html',
  styleUrl: './home-page.css',
})
export class HomePage {
  private readonly router = inject(Router);

  protected onLoggedOut(): void {
    this.router.navigateByUrl('/login');
  }
}
