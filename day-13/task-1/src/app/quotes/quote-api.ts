import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Quote } from './quote';

// Real contract: GET /api/quotes, mapped from QuotesApi/Program.cs:361-362.
const QUOTES_ENDPOINT = '/api/quotes';

@Injectable({
  providedIn: 'root',
})
export class QuoteApi {
  private readonly http = inject(HttpClient);

  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(QUOTES_ENDPOINT);
  }
}
