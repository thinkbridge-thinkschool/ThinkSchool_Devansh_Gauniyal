/**
 * Endpoints used, read from day-3/task-3/QuotesApi/Program.cs:
 *   GET  /api/quotes -> Quote[]  (Program.cs:361-362, no auth required)
 *   POST /api/quotes -> Quote    (Program.cs:364-376, requires the
 *     CanEditQuotes authorization policy -- scope claim "quotes.write")
 * Fields, read from day-3/task-3/QuotesApi/Quotes/Quote.cs:
 *   public sealed record Quote(int Id, string OwnerId, string Text);
 *   -> wire shape { id, ownerId, text } (camelCase; no JSON naming-policy override
 *      exists in Program.cs, and this was confirmed live in Day 13 Task 1).
 * Request DTO, read from day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs:
 *   public sealed record CreateQuoteRequest(string Text, string? Author = null);
 *   -> no validation attributes at all on either field; see create-quote-request.ts.
 *   Author was added 2026-08-25 at Devansh's explicit request.
 *
 * There is no per-item GET route on this API (day-3/task-3/QuotesApi/Program.cs has
 * no `MapGet` with a route parameter — only PUT/DELETE `/api/quotes/{id:int}`, both
 * auth-gated mutations). getQuoteDetail() below re-calls the one real read endpoint,
 * GET /api/quotes, and resolves the requested item client-side, so the "detail" fetch
 * is still a genuine, independently-timed HTTP round trip rather than a synchronous
 * lookup into the already-loaded list.
 *
 * createQuote() below never runs against a live server here (the route needs a
 * "quotes.write" JWT scope this app never obtains or hardcodes) -- it is only ever
 * exercised through HttpTestingController in tests.
 */
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { CreateQuoteRequest } from './create-quote-request';
import { Quote } from './quote';

const QUOTES_ENDPOINT = '/api/quotes';

@Injectable({
  providedIn: 'root',
})
export class QuoteApi {
  private readonly http = inject(HttpClient);

  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(QUOTES_ENDPOINT);
  }

  getQuoteDetail(id: number): Observable<Quote | undefined> {
    return this.http
      .get<Quote[]>(QUOTES_ENDPOINT)
      .pipe(map((quotes) => quotes.find((quote) => quote.id === id)));
  }

  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(QUOTES_ENDPOINT, request);
  }
}
