import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, withComponentInputBinding, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { QuoteDetailPage } from './quote-detail-page';
import { Quote } from '../quote';

const sample: Quote[] = [
  { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
  { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.', author: 'Marcus Aurelius' },
];

// 'quotes'/'quotes:id' are children of '' (HomePage) -- see app.routes.ts -- so every
// navigation here also mounts HomePage's own QuoteBrowser, which independently fetches
// GET /api/quotes for the list. That means most navigations here produce TWO pending
// requests to the same URL, not one; this helper flushes every currently-pending match.
function flushAllQuotesRequests(httpMock: HttpTestingController, data: Quote[] = sample): void {
  for (const req of httpMock.match('/api/quotes')) {
    req.flush(data);
  }
}

describe('QuoteDetailPage (routed, guarded, lazy)', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // withComponentInputBinding() must match app.config.ts's real provideRouter()
        // call -- without it, QuoteDetailPage.id() never receives the route param at
        // all (see verification-log.md for how this surfaced as a real test failure).
        provideRouter(routes, withComponentInputBinding()),
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    localStorage.clear();
    httpMock.verify();
  });

  // --- GUARD, exercised through real navigation ---

  it('GUARD PASS: an authenticated navigation to /quotes/:id activates the route and renders the detail', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/quotes/1');
    flushAllQuotesRequests(httpMock);
    harness.detectChanges();

    const el = harness.routeNativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="detail-page-content"]')).toBeTruthy();
    expect(el.querySelector('.quote-detail-page__text')?.textContent).toContain('Security is a process.');
  });

  it('GUARD REDIRECT: an unauthenticated navigation to /quotes/:id is redirected to "/login" and renders the real login page, not a blank detail route', async () => {
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/quotes/1');

    expect(router.url).toBe('/login');
    const el = harness.routeNativeElement as HTMLElement;
    expect(el.querySelector('app-dev-login')).toBeTruthy();
    expect(el.querySelector('[data-testid="detail-page-content"]')).toBeFalsy();
  });

  // --- the three route-param edges, each distinct ---

  it('PARAM MISSING: navigating to the paramless /quotes route renders the missing-id state, not a crash', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/quotes');
    // QuoteDetailPage never calls the API for a missing id, but HomePage's QuoteBrowser
    // still does for the list -- flush that one request so httpMock.verify() is clean.
    flushAllQuotesRequests(httpMock);
    harness.detectChanges();

    const el = harness.routeNativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="detail-page-status-missing"]')).toBeTruthy();
  });

  it('PARAM MALFORMED: a non-numeric id renders the malformed state, distinct from missing or not-found', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/quotes/abc');
    flushAllQuotesRequests(httpMock);
    harness.detectChanges();

    const el = harness.routeNativeElement as HTMLElement;
    const malformed = el.querySelector('[data-testid="detail-page-status-malformed"]');
    expect(malformed).toBeTruthy();
    expect(malformed!.textContent).toContain('abc');
  });

  it('PARAM WELL-FORMED BUT NOT FOUND: a numeric id with no matching quote renders not-found, distinct from malformed', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/quotes/9999');
    flushAllQuotesRequests(httpMock);
    harness.detectChanges();

    const el = harness.routeNativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="detail-page-status-not-found"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="detail-page-status-malformed"]')).toBeFalsy();
  });

  // --- View Transitions fallback ---

  it('VIEW TRANSITION FALLBACK: navigation completes and renders normally when the browser has no View Transitions API', async () => {
    // Not simulated by deleting anything -- jsdom (this test's real DOM environment)
    // does not implement document.startViewTransition at all, so every navigation in
    // this entire spec file already exercises withViewTransitions()'s no-support
    // fallback for real. This test just makes that fact explicit and re-confirms
    // navigation still completes correctly under it.
    expect(typeof (document as unknown as { startViewTransition?: unknown }).startViewTransition).toBe(
      'undefined',
    );

    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();

    const activated = await harness.navigateByUrl('/quotes/1');
    flushAllQuotesRequests(httpMock);
    harness.detectChanges();

    expect(activated).toBeTruthy();
    const el = harness.routeNativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="detail-page-content"]')).toBeTruthy();
  });
});

// Component-level stale-response guard test, mirroring the pattern the carried Day 13
// QuoteBrowser RACE test used (component created directly, id driven via setInput())
// rather than full router navigation -- this isolates the guard itself from the
// double-request complexity nested routing introduces above (see flushAllQuotesRequests).
describe('QuoteDetailPage stale-response guard (component-level)', () => {
  let fixture: ComponentFixture<QuoteDetailPage>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteDetailPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(QuoteDetailPage);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('RACE: discards a stale detail response when the id changes before it resolves (id 1, then id 2, flush 1 last -> shows 2)', () => {
    fixture.componentRef.setInput('id', '1');
    fixture.detectChanges();

    fixture.componentRef.setInput('id', '2');
    fixture.detectChanges();

    const pending = httpMock.match('/api/quotes');
    expect(pending.length).toBe(2);

    // Flush OUT OF ORDER: id=2's response resolves first, id=1's resolves last.
    pending[1].flush(sample);
    pending[0].flush(sample);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.quote-detail-page__text')?.textContent).toContain('Policies make intent explicit.');
  });
});
