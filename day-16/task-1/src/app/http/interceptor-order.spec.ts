/**
 * Proves the interceptor order this app registers -- authHeaderInterceptor,
 * errorMappingInterceptor, retryTransientGetInterceptor (see app.config.ts for the
 * full reasoning) -- actually behaves as documented: the auth header survives a retry,
 * and the error mapper sees only the FINAL outcome of the retry loop, never an
 * intermediate failure a later attempt went on to fix.
 */
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_INTERCEPTORS } from './api-interceptors';
import { AppHttpError } from './app-http-error';
import { AuthTokenService } from './auth-token.service';

const FAKE_TOKEN = 'test-token';

describe('interceptor order: auth + errorMapping + retry together', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        // Exercises the SAME order and the SAME interceptor instances app.config.ts
        // registers in production (via API_INTERCEPTORS) -- not a hand-copied list
        // that could silently drift out of sync with the real wiring.
        provideHttpClient(withInterceptors([...API_INTERCEPTORS])),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: { getToken: () => FAKE_TOKEN } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('a request that fails once then succeeds surfaces NO error to the caller, and every retried attempt still carries the auth header', async () => {
    let result: unknown;
    let error: unknown;
    http.get('/api/quotes').subscribe({
      next: (body) => (result = body),
      error: (err) => (error = err),
    });

    const first = httpMock.expectOne('/api/quotes');
    expect(first.request.headers.get('Authorization')).toBe(`Bearer ${FAKE_TOKEN}`);
    first.flush('boom', { status: 500, statusText: 'Server Error' });

    await vi.advanceTimersByTimeAsync(200);

    const second = httpMock.expectOne('/api/quotes');
    expect(second.request.headers.get('Authorization')).toBe(`Bearer ${FAKE_TOKEN}`);
    const fixture = [{ id: 1, ownerId: 'user-1', text: 'Security is a process.' }];
    second.flush(fixture);

    expect(result).toEqual(fixture);
    expect(error).toBeUndefined();
  });

  it('a request that exhausts all retries surfaces EXACTLY ONE mapped AppHttpError, not a raw intermediate failure', async () => {
    let error: unknown;
    let errorEmissions = 0;
    http.get('/api/quotes').subscribe({
      error: (err) => {
        errorEmissions += 1;
        error = err;
      },
    });

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(200);

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(400);

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(1000);

    httpMock.expectNone('/api/quotes');
    expect(errorEmissions).toBe(1);
    expect(error).toBeInstanceOf(AppHttpError);
    expect((error as AppHttpError).status).toBe(500);
  });
});
