/**
 * The full, ordered list of this app's own HTTP interceptors, in the exact order
 * app.config.ts registers them via provideHttpClient(withInterceptors(...)). Exported
 * as one array so interceptor-order.spec.ts exercises the SAME order production uses,
 * not a copy that could silently drift from it. See app.config.ts for the reasoning
 * behind this order.
 */
import { HttpInterceptorFn } from '@angular/common/http';
import { authHeaderInterceptor } from './auth-header.interceptor';
import { errorMappingInterceptor } from './error-mapping.interceptor';
import { retryTransientGetInterceptor } from './retry.interceptor';

export const API_INTERCEPTORS: HttpInterceptorFn[] = [
  authHeaderInterceptor,
  errorMappingInterceptor,
  retryTransientGetInterceptor,
];
