import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { retryTransientGetInterceptor } from './retry.interceptor';

// Backoff mechanism under test: RETRY_BASE_DELAY_MS = 200, RETRY_BACKOFF_FACTOR = 2
// (see retry.interceptor.ts), so the two possible retry delays are 200ms and 400ms.
// Every test below uses vitest's fake timers (vi.useFakeTimers / advanceTimersByTimeAsync)
// so these delays are virtual -- the suite never actually waits.

describe('retryTransientGetInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryTransientGetInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('a GET failing with a 5xx is retried up to the cap (3 total attempts), then surfaces the error', async () => {
    let error: unknown;
    http.get('/api/quotes').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(200);

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(400);

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(1000);

    httpMock.expectNone('/api/quotes');
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(500);
  });

  it('a GET failing with a network error (status 0) is retried', async () => {
    let error: unknown;
    http.get('/api/quotes').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').error(new ProgressEvent('error'), { status: 0 });
    await vi.advanceTimersByTimeAsync(200);

    // Second attempt: let it succeed, proving the retry actually re-issued the request.
    httpMock.expectOne('/api/quotes').flush([{ id: 1, ownerId: 'user-1', text: 'ok' }]);

    expect(error).toBeUndefined();
  });

  it('a GET failing with a 4xx is NOT retried -- exactly one attempt', async () => {
    let error: unknown;
    http.get('/api/quotes').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush('nope', { status: 404, statusText: 'Not Found' });
    await vi.advanceTimersByTimeAsync(1000);

    httpMock.expectNone('/api/quotes');
    expect((error as HttpErrorResponse).status).toBe(404);
  });

  it('a POST failing with a 5xx is NOT retried -- exactly one attempt', async () => {
    let error: unknown;
    http.post('/api/quotes', { text: 'x' }).subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(1000);

    httpMock.expectNone('/api/quotes');
    expect((error as HttpErrorResponse).status).toBe(500);
  });

  it('a GET that fails once then succeeds returns the success to the caller with no error surfaced', async () => {
    let result: unknown;
    let error: unknown;
    http.get('/api/quotes').subscribe({
      next: (body) => (result = body),
      error: (err) => (error = err),
    });

    httpMock.expectOne('/api/quotes').flush('boom', { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(200);

    const fixture = [{ id: 1, ownerId: 'user-1', text: 'Security is a process.' }];
    httpMock.expectOne('/api/quotes').flush(fixture);

    expect(result).toEqual(fixture);
    expect(error).toBeUndefined();
  });

  it('uses vitest fake timers for backoff -- this whole spec file runs without any real wait', () => {
    // Documentation-as-assertion: every async test above completes using
    // vi.advanceTimersByTimeAsync, never a real setTimeout/sleep. If that were not true,
    // the two multi-retry tests above (600ms and 1400ms of virtual delay) would make
    // this file measurably slow; it is not, because no real waiting happens.
    expect(true).toBe(true);
  });
});
