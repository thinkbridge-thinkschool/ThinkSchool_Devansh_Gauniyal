import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppHttpError } from './app-http-error';
import { errorMappingInterceptor } from './error-mapping.interceptor';

describe('errorMappingInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('maps a ProblemDetails 4xx to the typed app error with a friendly message', () => {
    let error: unknown;
    http.get('/api/quotes/999').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes/999').flush(
      { type: 'about:blank', title: 'Not Found', status: 404, detail: 'No quote with that id.' },
      { status: 404, statusText: 'Not Found' },
    );

    expect(error).toBeInstanceOf(AppHttpError);
    const mapped = error as AppHttpError;
    expect(mapped.status).toBe(404);
    expect(mapped.friendlyMessage).toBe('No quote with that id.');
    expect(mapped.fieldErrors).toBeNull();
  });

  it('maps a ValidationProblemDetails 4xx and preserves the per-field errors', () => {
    let error: unknown;
    http.post('/api/quotes', {}).subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { text: ['Text is required.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(error).toBeInstanceOf(AppHttpError);
    const mapped = error as AppHttpError;
    expect(mapped.friendlyMessage).toBe('One or more validation errors occurred.');
    expect(mapped.fieldErrors).toEqual({ text: ['Text is required.'] });
  });

  it('maps a 4xx that is NOT ProblemDetails (the real API: empty body) to a sane typed error instead of throwing', () => {
    // This is the real behaviour of day-3/task-3/QuotesApi, not a hypothetical: every
    // observed 4xx from it has an empty body (see output/headers-401-post-unauth.txt,
    // output/headers-405-quotes-id.txt). A gateway returning plain text falls down the
    // same path.
    let error: unknown;
    http.post('/api/quotes', { text: 'x' }).subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(error).toBeInstanceOf(AppHttpError);
    const mapped = error as AppHttpError;
    expect(mapped.status).toBe(401);
    expect(mapped.friendlyMessage).toBe('You need to sign in to do that.');
    expect(mapped.fieldErrors).toBeNull();
  });

  it('maps a plain-text (non-JSON) error body to a sane typed error instead of throwing', () => {
    let error: unknown;
    http.get('/api/quotes').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').flush('<html>Bad Gateway</html>', {
      status: 502,
      statusText: 'Bad Gateway',
    });

    expect(error).toBeInstanceOf(AppHttpError);
    const mapped = error as AppHttpError;
    expect(mapped.status).toBe(502);
    expect(mapped.friendlyMessage).toBe(
      'The server had a problem handling that request. Please try again.',
    );
  });

  it('maps a network error (status 0) to a sane typed error', () => {
    let error: unknown;
    http.get('/api/quotes').subscribe({ error: (err) => (error = err) });

    httpMock.expectOne('/api/quotes').error(new ProgressEvent('error'), { status: 0 });

    expect(error).toBeInstanceOf(AppHttpError);
    expect((error as AppHttpError).friendlyMessage).toBe(
      'Could not reach the server. Check your connection and try again.',
    );
  });
});
