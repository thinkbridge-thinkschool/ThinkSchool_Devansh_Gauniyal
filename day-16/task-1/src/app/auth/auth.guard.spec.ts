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

  it('UNAUTHENTICATED: returns a UrlTree redirecting to "/" (not a boolean) when no token is present', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(result instanceof UrlTree).toBe(true);
    expect((result as UrlTree).toString()).toBe('/');
  });

  it('ROUTE CONFIG: authGuard is actually attached to both quote-detail routes, not just defined in isolation', () => {
    const quotesRoute = routes.find((r) => r.path === 'quotes');
    const quotesIdRoute = routes.find((r) => r.path === 'quotes/:id');

    expect(quotesRoute?.canActivate).toContain(authGuard);
    expect(quotesIdRoute?.canActivate).toContain(authGuard);
  });
});
