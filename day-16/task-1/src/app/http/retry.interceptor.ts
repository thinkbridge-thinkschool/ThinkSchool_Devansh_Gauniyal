/**
 * Single responsibility: retry ONLY idempotent GET requests, ONLY on transient failures
 * (a network error, status 0, or a 5xx) — never a 4xx, never a non-GET verb. A 4xx will
 * not change on repeat, and retrying a non-idempotent verb (POST/PUT/PATCH) risks
 * duplicating a write, so both are excluded structurally, not just by convention.
 *
 * Cap: RETRY_MAX_ATTEMPTS = 3 total attempts (1 initial + up to 2 retries).
 * Backoff: exponential, base RETRY_BASE_DELAY_MS = 200ms, factor RETRY_BACKOFF_FACTOR =
 * 2 (attempt delays: 200ms, then 400ms).
 *
 * Registered LAST (innermost) in provideHttpClient(withInterceptors([...])) — see
 * app.config.ts — so its retries happen closest to the network and stay invisible to
 * authHeaderInterceptor and errorMappingInterceptor above it: each retry re-invokes the
 * same `next(req)` this interceptor was given, without flowing back out through those
 * outer interceptors, so they only ever see this interceptor's final settled result.
 */
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

const RETRY_MAX_ATTEMPTS = 3;
const RETRY_BASE_DELAY_MS = 200;
const RETRY_BACKOFF_FACTOR = 2;

function isTransientFailure(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
}

export const retryTransientGetInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: RETRY_MAX_ATTEMPTS - 1,
      delay: (error, retryAttempt) => {
        if (!isTransientFailure(error)) {
          return throwError(() => error);
        }
        const delayMs = RETRY_BASE_DELAY_MS * Math.pow(RETRY_BACKOFF_FACTOR, retryAttempt - 1);
        return timer(delayMs);
      },
    }),
  );
};
