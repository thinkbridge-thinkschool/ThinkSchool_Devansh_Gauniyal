/**
 * Functional route guard (CanActivateFn) for the lazy-loaded quote detail routes (see
 * ../app.routes.ts). Checks the SAME token source auth-header.interceptor.ts already
 * reads -- AuthTokenService, backed by the devAuthToken localStorage key dev-login.ts
 * writes -- rather than a second, competing notion of "signed in". No token is ever
 * hardcoded here.
 *
 * Returns `true` when a token is present, so navigation proceeds. Returns a UrlTree
 * (redirecting to '/') when it is not, so the navigation is cancelled and replaced
 * atomically. This deliberately does NOT call router.navigate() -- a guard that returns
 * false and separately calls navigate() lets the in-flight (blocked) navigation and the
 * new (redirect) navigation race each other; returning a UrlTree lets the router itself
 * treat the redirect as the resolved outcome of the same navigation, with no race and no
 * blank intermediate screen.
 *
 * "Authenticated" here means only "a devAuthToken is present in localStorage" -- the
 * same local-dev convenience Day 15 already built, not a real authentication system. See
 * README.md, "What 'authenticated' means here", for the full interpretation and its
 * limits.
 */
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthTokenService } from '../http/auth-token.service';

export const authGuard: CanActivateFn = (): true | UrlTree => {
  const hasToken = !!inject(AuthTokenService).getToken();
  if (hasToken) {
    return true;
  }
  return inject(Router).createUrlTree(['/']);
};
