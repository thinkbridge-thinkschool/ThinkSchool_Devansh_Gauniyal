import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

describe('app.routes structure', () => {
  it('STRUCTURE: both quote-detail routes (nested under "") use loadComponent, not an eagerly-imported component', () => {
    const homeRoute = routes.find((r) => r.path === '');
    const quotesRoute = homeRoute?.children?.find((r) => r.path === 'quotes');
    const quotesIdRoute = homeRoute?.children?.find((r) => r.path === 'quotes/:id');

    expect(quotesRoute?.loadComponent).toBeTruthy();
    expect(quotesRoute && 'component' in quotesRoute).toBe(false);
    expect(quotesIdRoute?.loadComponent).toBeTruthy();
    expect(quotesIdRoute && 'component' in quotesIdRoute).toBe(false);
  });

  it('STRUCTURE: "/login" and "" (home) also use loadComponent, not an eagerly-imported component', () => {
    const loginRoute = routes.find((r) => r.path === 'login');
    const homeRoute = routes.find((r) => r.path === '');

    expect(loginRoute?.loadComponent).toBeTruthy();
    expect(loginRoute && 'component' in loginRoute).toBe(false);
    expect(homeRoute?.loadComponent).toBeTruthy();
    expect(homeRoute && 'component' in homeRoute).toBe(false);
  });
});

describe('app.routes redirect behavior (real navigation)', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    localStorage.clear();
    httpMock.verify();
  });

  it('REDIRECT TO LOGIN: an unauthenticated visit to "/" ends up at "/login"', async () => {
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/');

    expect(router.url).toBe('/login');
  });

  it('REDIRECT TO HOME: an already-authenticated visit to "/login" ends up at "/"', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/login');
    httpMock.expectOne('/api/quotes').flush([]);

    expect(router.url).toBe('/');
  });

  it('AUTHENTICATED HOME: an authenticated visit to "/" renders HomePage, not a redirect', async () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/');
    httpMock.expectOne('/api/quotes').flush([]);

    expect(router.url).toBe('/');
    expect(harness.routeNativeElement?.querySelector('app-quote-browser')).toBeTruthy();
  });
});
