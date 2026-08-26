/**
 * DEMO ONLY -- not part of the graded interceptor work (auth/error-mapping/retry).
 * Counts every request that actually leaves toward the network, including each retry
 * attempt. Must be registered LAST (closest to the backend, after
 * retryTransientGetInterceptor) so every retried attempt is counted too -- if it were
 * registered earlier, retry's internal resubscription would never flow back out past
 * it, and this would always read 1 regardless of how many real attempts happened.
 */
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { RequestCounterService } from './request-counter.service';

export const requestCounterInterceptor: HttpInterceptorFn = (req, next) => {
  inject(RequestCounterService).increment();
  return next(req);
};
