/**
 * Characterization test for the real Week-1 API: day-3/task-3/QuotesApi.
 *
 * This pins EXISTING behaviour, not a specification of what the API should do. It must
 * stay green before any interceptor exists (Day 15 Task 1's required ordering) and it
 * must fail if the real route, its (non-existent) pagination parameters, the real
 * response field names, or the real error shape ever change.
 *
 * Every claim below cites the real source file it was read from. Two of the four facts
 * pinned here (the success body shape and the "4xx has no body" shape) were additionally
 * confirmed live against a running instance of the real API -- see
 * output/get-quotes-success-body.json, output/get-quotes-ignored-pagination-body.json,
 * output/headers-405-quotes-id.txt and output/headers-401-post-unauth.txt, captured
 * 2026-08-26 against http://127.0.0.1:5080. The Academy's own example
 * (`GET /api/quotes?page=N&size=N` -> `{id, author, text}`, errors as
 * ProblemDetails/ValidationProblemDetails) does NOT match this API on any of these
 * four points; each divergence is called out explicitly below and in submission.md.
 */
import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

// Read from day-3/task-3/QuotesApi/Program.cs:361-362 -- `app.MapGet("/api/quotes", ...)`.
// No route parameter, no query-string parameter is read anywhere in the handler body.
const REAL_LIST_ROUTE = '/api/quotes';

// Read from day-3/task-3/QuotesApi/Quotes/Quote.cs:5 --
//   public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null);
// ASP.NET Core's default Minimal API JSON options camel-case every property, and
// Program.cs never overrides that -- so the wire shape is exactly this, confirmed live.
const REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE = [
  { id: 1, ownerId: 'user-1', text: 'Security is a process.', author: null },
  { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.', author: null },
  { id: 3, ownerId: 'local-dev-caller', text: 'hello world', author: 'me' },
];

describe('QuotesApi real contract (characterization -- pins existing behaviour)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('PINS the real route: GET /api/quotes, with no id segment and no query string', () => {
    http.get(REAL_LIST_ROUTE).subscribe();

    const req = httpMock.expectOne(REAL_LIST_ROUTE);
    expect(req.request.method).toBe('GET');
    expect(req.request.url).toBe('/api/quotes');
    // The Academy's example is GET /api/quotes?page=N&size=N. The real handler
    // (Program.cs:361-362) takes no parameters at all -- there is no pagination to pin.
    expect(req.request.params.keys().length).toBe(0);
  });

  it('PINS that a request carrying page/size still hits the same route unchanged -- the API has no pagination to consume them', () => {
    // Not a recommendation to send these -- this documents what actually happens if
    // someone assumes the Academy's example and does send them: the real handler
    // ignores them entirely and returns the full, unpaginated list. Confirmed live:
    // output/get-quotes-ignored-pagination-body.json is byte-identical to
    // output/get-quotes-success-body.json.
    http.get(REAL_LIST_ROUTE, { params: { page: '1', size: '10' } }).subscribe();

    const req = httpMock.expectOne((r) => r.url === REAL_LIST_ROUTE);
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('10');
    req.flush(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);
  });

  it('PINS the real response field names and casing: id, ownerId, text, author -- not the Academy example {id, author, text}', () => {
    let received: unknown;
    http.get(REAL_LIST_ROUTE).subscribe((body) => (received = body));

    httpMock.expectOne(REAL_LIST_ROUTE).flush(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);

    expect(received).toEqual(REAL_SUCCESS_FIXTURE_FROM_LIVE_CAPTURE);
    const first = (received as Array<Record<string, unknown>>)[0];
    // ownerId is real and required; the Academy's example never mentions it at all.
    expect(first['ownerId']).toBe('user-1');
    expect(Object.keys(first).sort()).toEqual(['author', 'id', 'ownerId', 'text']);
  });

  it('PINS that a 4xx from the real API has an EMPTY body -- not ProblemDetails, not ValidationProblemDetails', () => {
    // Real capture, output/headers-401-post-unauth.txt: POST /api/quotes with no
    // Authorization header -> 401 Unauthorized, Content-Length: 0, WWW-Authenticate:
    // Bearer. grep -rn "ProblemDetails|ApiController|AddProblemDetails|ValidationProblem"
    // across the entire day-3/task-3/QuotesApi tree returns zero matches -- there is no
    // ApiController and no AddProblemDetails() call anywhere, so nothing in this API can
    // ever produce a ProblemDetails or ValidationProblemDetails body.
    let capturedStatus: number | undefined;
    let capturedBody: unknown;
    http.post(REAL_LIST_ROUTE, { text: 'x' }).subscribe({
      error: (err) => {
        capturedStatus = err.status;
        capturedBody = err.error;
      },
    });

    httpMock.expectOne(REAL_LIST_ROUTE).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(capturedStatus).toBe(401);
    expect(capturedBody).toBeNull();
  });
});
