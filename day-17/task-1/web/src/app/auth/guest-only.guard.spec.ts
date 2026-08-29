import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { guestOnlyGuard } from './guest-only.guard';
import { routes } from '../app.routes';

const route = {} as ActivatedRouteSnapshot;
const state = {} as RouterStateSnapshot;

describe('guestOnlyGuard', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('NO TOKEN: returns true when there is no devAuthToken, so the login page renders', () => {
    const result = TestBed.runInInjectionContext(() => guestOnlyGuard(route, state));

    expect(result).toBe(true);
  });

  it('ALREADY SIGNED IN: returns a UrlTree redirecting to "/" (not a boolean) when a devAuthToken is present', () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');

    const result = TestBed.runInInjectionContext(() => guestOnlyGuard(route, state));

    expect(result instanceof UrlTree).toBe(true);
    expect((result as UrlTree).toString()).toBe('/');
  });

  it('ROUTE CONFIG: guestOnlyGuard is actually attached to "/login", not just defined in isolation', () => {
    const loginRoute = routes.find((r) => r.path === 'login');

    expect(loginRoute?.canActivate).toContain(guestOnlyGuard);
  });
});
