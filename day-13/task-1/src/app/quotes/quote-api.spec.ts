import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteApi } from './quote-api';

describe('QuoteApi', () => {
  let service: QuoteApi;
  let httpMock: HttpTestingController;

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

  it('requests the real endpoint, GET /api/quotes', () => {
    service.getQuotes().subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('parses a fixture using the real DTO field names (id, ownerId, text)', () => {
    // Field names copied verbatim from QuotesApi.Quotes.Quote at
    // day-3/task-3/QuotesApi/Quotes/Quote.cs, camelCased per ASP.NET Core's
    // default Minimal API JSON naming policy.
    const fixture = [
      { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
      { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.' },
    ];

    let received: unknown;
    service.getQuotes().subscribe((quotes) => (received = quotes));
    httpMock.expectOne('/api/quotes').flush(fixture);

    expect(received).toEqual(fixture);
    expect((received as typeof fixture)[0].ownerId).toBe('user-1');
    expect((received as typeof fixture)[1].text).toBe('Policies make intent explicit.');
  });

  it('does not populate the model from a fixture with a wrong field name', () => {
    // Deliberately wrong: "owner" instead of the real "ownerId". If the binding
    // were coincidental (e.g. relying on `any` and not the real Quote shape),
    // this would silently "work" too. It does not: ownerId comes back undefined,
    // proving the contract is the real one, not a guess that happens to match.
    const wrongShapeFixture = [{ id: 1, owner: 'user-1', text: 'Security is a process.' }];

    let received: unknown;
    service.getQuotes().subscribe((quotes) => (received = quotes));
    httpMock.expectOne('/api/quotes').flush(wrongShapeFixture);

    const quote = (received as unknown[])[0] as Record<string, unknown>;
    expect(quote['ownerId']).toBeUndefined();
    expect(quote['owner']).toBe('user-1');
  });
});
