/**
 * Functional route guard (CanActivateFn) for the '/login' route: the inverse of
 * authGuard (../auth.guard.ts). Checks the same AuthTokenService token source.
 *
 * Returns `true` (render the login page) when there is NO token. Returns a UrlTree
 * redirecting to '/' when there already is one, so a signed-in user who navigates to
 * /login directly (bookmark, back button, typed URL) is sent straight to the app instead
 * of seeing the sign-in form again.
 */
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthTokenService } from '../http/auth-token.service';

export const guestOnlyGuard: CanActivateFn = (): true | UrlTree => {
  const hasToken = !!inject(AuthTokenService).getToken();
  if (!hasToken) {
    return true;
  }
  return inject(Router).createUrlTree(['/']);
};
