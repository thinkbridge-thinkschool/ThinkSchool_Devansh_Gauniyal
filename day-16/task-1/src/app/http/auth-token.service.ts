import { Injectable } from '@angular/core';

// Same localStorage key dev-login.ts, dev-token.interceptor.ts and app.ts already read
// and write (see those files) -- one real source of truth for "is there a token", not a
// second competing one. No token value is ever hardcoded here; only this key NAME is a
// literal, and a key name is not a secret.
const DEV_AUTH_TOKEN_KEY = 'devAuthToken';

@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  getToken(): string | null {
    return localStorage.getItem(DEV_AUTH_TOKEN_KEY);
  }
}
