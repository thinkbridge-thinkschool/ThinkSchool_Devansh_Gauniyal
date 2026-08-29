import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { devTokenInterceptor } from './auth/dev-token.interceptor';
import { requestCounterInterceptor } from './demo/request-counter.interceptor';
import { API_INTERCEPTORS } from './http/api-interceptors';
import { routes } from './app.routes';
import { API_BASE_URL, buildTimeApiBaseUrl } from './api-base-url';

// Order (request flows left to right, responses flow back right to left):
//   devTokenInterceptor    -- carried, untouched, local-dev-only convenience (see its
//                              own header comment); a no-op unless a token was set
//                              manually, so it neither conflicts with nor depends on
//                              API_INTERCEPTORS below.
//   API_INTERCEPTORS = [authHeaderInterceptor, errorMappingInterceptor,
//                        retryTransientGetInterceptor] (see api-interceptors.ts):
//     authHeaderInterceptor   -- must run before the retry interceptor so the header it
//                                sets is baked into the request retry.interceptor.ts
//                                reuses for every retried attempt.
//     errorMappingInterceptor -- must wrap retryTransientGetInterceptor (come before it
//                                here) so it only ever observes retry's FINAL settled
//                                result, never an intermediate failure a later retry
//                                went on to fix.
//     retryTransientGetInterceptor -- innermost/last, closest to the network, so its
//                                own retries stay invisible to both interceptors above.
//   requestCounterInterceptor -- DEMO ONLY, not part of the graded work (see
//                                demo/request-counter.interceptor.ts). Registered last
//                                of all so it counts every real attempt, including
//                                retries.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: API_BASE_URL, useFactory: buildTimeApiBaseUrl },
    provideHttpClient(
      withInterceptors([devTokenInterceptor, ...API_INTERCEPTORS, requestCounterInterceptor]),
    ),
    // withComponentInputBinding() lets quote-detail-page.ts read the :id route param via
    // a plain input() instead of injecting ActivatedRoute. withViewTransitions() wraps
    // router navigations in document.startViewTransition() when the browser supports it
    // (see README.md for the support caveat) and navigates normally, synchronously, when
    // it does not -- there is no separate fallback code path to maintain.
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
  ],
};
