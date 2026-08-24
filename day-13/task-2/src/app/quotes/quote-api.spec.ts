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
});
