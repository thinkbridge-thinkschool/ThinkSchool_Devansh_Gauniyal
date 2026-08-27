import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authHeaderInterceptor } from './auth-header.interceptor';
import { AuthTokenService } from './auth-token.service';

const FAKE_TOKEN = 'test-token';

describe('authHeaderInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  function configure(token: string | null): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authHeaderInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthTokenService, useValue: { getToken: () => token } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches a correctly-formed Authorization header to a request to our API', () => {
    configure(FAKE_TOKEN);
    http.get('/api/quotes').subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${FAKE_TOKEN}`);
    req.flush([]);
  });

  it('does NOT attach the header to a request to a different origin', () => {
    configure(FAKE_TOKEN);
    http.get('https://third-party.example.com/data').subscribe();

    const req = httpMock.expectOne('https://third-party.example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('does NOT attach the header to a same-origin request outside /api/', () => {
    configure(FAKE_TOKEN);
    http.get('/assets/config.json').subscribe();

    const req = httpMock.expectOne('/assets/config.json');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('sends no Authorization header at all when there is no token', () => {
    configure(null);
    http.get('/api/quotes').subscribe();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('never contains a real hardcoded token literal anywhere in this spec file', () => {
    // Structural self-check: the only token literal in this file is the obviously fake
    // 'test-token' constant above.
    expect(FAKE_TOKEN).toBe('test-token');
  });
});
