/**
 * Single responsibility: turn a failed HttpErrorResponse into a typed AppHttpError with
 * a friendly message (see app-http-error.ts), preserving per-field errors for a
 * ValidationProblemDetails body.
 *
 * Registered SECOND in provideHttpClient(withInterceptors([...])) — between
 * authHeaderInterceptor and retryTransientGetInterceptor — so it wraps
 * retryTransientGetInterceptor and only ever observes the FINAL settled outcome of that
 * interceptor's own internal retry loop (its retries call the shared `next` directly and
 * never re-enter this interceptor), never an intermediate failure that a later retry
 * attempt went on to fix.
 */
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { mapHttpErrorToAppError } from './app-http-error';

export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        return throwError(() => mapHttpErrorToAppError(err));
      }
      return throwError(() => err);
    }),
  );
