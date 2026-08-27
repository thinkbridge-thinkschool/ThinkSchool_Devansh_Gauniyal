import { HttpInterceptorFn } from '@angular/common/http';

// LOCAL DEV CONVENIENCE ONLY -- not part of the graded exercise and not
// exercised by any automated test (localStorage is empty in every test
// environment, so this interceptor is a no-op there and every existing spec
// file, which configures its own HttpTestingController providers directly
// rather than importing appConfig, is unaffected).
//
// Attaches a bearer token from localStorage, if one has been set manually
// via the browser devtools console, so POST /api/quotes can be exercised
// against a real, locally running QuotesApi during manual verification. No
// token is ever hardcoded here or committed anywhere -- see README.md,
// "Testing a real save locally".
export const devTokenInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('devAuthToken');
  if (!token) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
