/**
 * Single responsibility: attach `Authorization: Bearer <token>` to requests going to
 * this app's own API (same origin, path under /api/) and to nothing else. The token
 * comes from AuthTokenService, never a hardcoded string, and is never committed. If
 * there is no token, the request goes out unmodified rather than with an empty header.
 *
 * Registered FIRST (outermost) in provideHttpClient(withInterceptors([...])) — see
 * app.config.ts for the full order and why — so the header is baked into the request
 * object once, before it ever reaches retryTransientGetInterceptor. RxJS `retry` re-uses
 * that same already-cloned request for every retried attempt, so every retry still
 * carries the header without this interceptor needing to run again.
 */
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthTokenService } from './auth-token.service';

function isRequestToOurApi(url: string): boolean {
  const resolved = new URL(url, location.origin);
  return resolved.origin === location.origin && resolved.pathname.startsWith('/api/');
}

export const authHeaderInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isRequestToOurApi(req.url)) {
    return next(req);
  }

  const token = inject(AuthTokenService).getToken();
  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
