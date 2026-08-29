import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { routes } from '../app.routes';

// Dummy args for the CanActivateFn signature -- authGuard reads only the injected
// AuthTokenService, never route or state, so empty stand-ins are sufficient.
const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

describe('authGuard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('AUTHENTICATED: returns true when a devAuthToken is present, so navigation proceeds', () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(result).toBe(true);
  });

  it('UNAUTHENTICATED: returns a UrlTree redirecting to "/login" (not a boolean) when no token is present', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(result instanceof UrlTree).toBe(true);
    expect((result as UrlTree).toString()).toBe('/login');
  });

  it('ROUTE CONFIG: authGuard is actually attached to "" (home) and both its quote-detail children, not just defined in isolation', () => {
    const homeRoute = routes.find((r) => r.path === '');
    const quotesRoute = homeRoute?.children?.find((r) => r.path === 'quotes');
    const quotesIdRoute = homeRoute?.children?.find((r) => r.path === 'quotes/:id');

    expect(homeRoute?.canActivate).toContain(authGuard);
    expect(quotesRoute?.canActivate).toContain(authGuard);
    expect(quotesIdRoute?.canActivate).toContain(authGuard);
  });
});
