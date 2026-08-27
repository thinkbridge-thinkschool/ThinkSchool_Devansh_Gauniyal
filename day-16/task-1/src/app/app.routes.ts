/**
 * Route table for the quote detail page. The real QuotesApi (day-3/task-3/QuotesApi,
 * see Program.cs:361-362) has no per-item GET route -- only GET /api/quotes (list, no
 * auth) and PUT/DELETE /api/quotes/{id:int} (auth-gated mutations). There is therefore
 * no real "detail endpoint" to route a param onto the API with; quote-api.ts's
 * getQuoteDetail() already works around this by re-calling the one real read endpoint
 * and resolving the id client-side, and quote-detail-page.ts does the same. The route
 * param below still exercises real router navigation and a real (client-side) lookup by
 * the real id field -- Quote.Id, a non-negative int (Quote.cs) -- it is just not backed
 * by a server-side detail route, because none exists.
 *
 * Both routes point at the SAME lazily-loaded component (quote-detail-page.ts):
 * 'quotes/:id' is the normal detail route; the paramless 'quotes' route exists so the
 * "missing route param" edge is exercised by a real navigation, not only a unit test
 * with the input left unset.
 */
import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: 'quotes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes/quote-detail-page/quote-detail-page').then((m) => m.QuoteDetailPage),
  },
  {
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes/quote-detail-page/quote-detail-page').then((m) => m.QuoteDetailPage),
  },
];
