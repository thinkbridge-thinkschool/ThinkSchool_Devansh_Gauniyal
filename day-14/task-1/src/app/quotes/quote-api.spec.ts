import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteApi } from './quote-api';

describe('QuoteApi', () => {
  let service: QuoteApi;
  let httpMock: HttpTestingController;

  const fixture = [
    { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
    { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.' },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuoteApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('requests the real endpoint, GET /api/quotes, for the list', () => {
    service.getQuotes().subscribe();
    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('parses a list fixture using the real DTO field names (id, ownerId, text)', () => {
    let received: unknown;
    service.getQuotes().subscribe((quotes) => (received = quotes));
    httpMock.expectOne('/api/quotes').flush(fixture);

    expect(received).toEqual(fixture);
    expect((received as typeof fixture)[0].ownerId).toBe('user-1');
  });

  it('does not populate the model from a fixture with a wrong field name', () => {
    // Deliberately wrong: "owner" instead of the real "ownerId".
    const wrongShapeFixture = [{ id: 1, owner: 'user-1', text: 'Security is a process.' }];

    let received: unknown;
    service.getQuotes().subscribe((quotes) => (received = quotes));
    httpMock.expectOne('/api/quotes').flush(wrongShapeFixture);

    const quote = (received as unknown[])[0] as Record<string, unknown>;
    expect(quote['ownerId']).toBeUndefined();
    expect(quote['owner']).toBe('user-1');
  });

  it('getQuoteDetail calls the same GET /api/quotes (no separate detail route exists) and resolves the matching id', () => {
    let received: unknown;
    service.getQuoteDetail(2).subscribe((quote) => (received = quote));

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('GET');
    req.flush(fixture);

    expect(received).toEqual(fixture[1]);
  });

  it('getQuoteDetail resolves undefined when the id is not present in the response', () => {
    let received: unknown;
    service.getQuoteDetail(999).subscribe((quote) => (received = quote));
    httpMock.expectOne('/api/quotes').flush(fixture);

    expect(received).toBeUndefined();
  });

  // --- Day 14: createQuote, extending this service rather than duplicating it ---

  it('createQuote POSTs to the real /api/quotes route with only the real "text" field', () => {
    service.createQuote({ text: 'Security is a process.' }).subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    // The real DTO (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs) has exactly
    // one field. A field the API does not have must appear nowhere in the body.
    expect(Object.keys(req.request.body)).toEqual(['text']);
    expect(req.request.body.text).toBe('Security is a process.');

    req.flush({ id: 3, ownerId: 'user-1', text: 'Security is a process.' });
  });

  it('createQuote resolves with the real response shape on success', () => {
    let result: unknown;
    service.createQuote({ text: 'Policies make intent explicit.' }).subscribe((quote) => {
      result = quote;
    });

    const req = httpMock.expectOne('/api/quotes');
    req.flush({ id: 4, ownerId: 'user-2', text: 'Policies make intent explicit.' });

    expect(result).toEqual({ id: 4, ownerId: 'user-2', text: 'Policies make intent explicit.' });
  });

  it('createQuote surfaces a server error to the caller instead of swallowing it', () => {
    let error: unknown;
    service.createQuote({ text: 'Anything' }).subscribe({
      error: (err) => {
        error = err;
      },
    });

    const req = httpMock.expectOne('/api/quotes');
    req.flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    expect(error).toBeTruthy();
  });
});
