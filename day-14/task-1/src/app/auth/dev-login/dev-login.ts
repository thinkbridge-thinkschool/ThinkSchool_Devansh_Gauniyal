import { HttpClient } from '@angular/common/http';
import { Component, inject, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

// LOCAL DEV CONVENIENCE ONLY -- not part of the graded exercise, not
// exercised by any automated test. Lets Devansh log in against a real,
// locally running QuotesApi (see README.md, "Testing a real save locally")
// without needing browser devtools: this form calls the real
// POST /api/auth/login and stores the resulting token under the same
// localStorage key dev-token.interceptor.ts reads. No credential is
// hardcoded here -- it is typed into the form at runtime.
//
// Renders as a full sign-in card when logged out, and a slim status bar
// with a log-out action when logged in; App gates the rest of the page on
// (loginSucceeded)/(loggedOut).
interface LoginResponse {
  access_token: string;
  expires_in: number;
}

@Component({
  selector: 'app-dev-login',
  imports: [ReactiveFormsModule],
  templateUrl: './dev-login.html',
  styleUrl: './dev-login.css',
})
export class DevLogin {
  private readonly http = inject(HttpClient);

  readonly loginSucceeded = output<void>();
  readonly loggedOut = output<void>();

  protected readonly status = signal<string | null>(null);
  protected readonly loggingIn = signal(false);
  protected readonly loggedIn = signal(!!localStorage.getItem('devAuthToken'));

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
  });

  protected login(): void {
    this.loggingIn.set(true);
    this.status.set(null);
    this.http
      .post<LoginResponse>('/api/auth/login', {
        email: this.form.controls.email.value,
        password: this.form.controls.password.value,
      })
      .subscribe({
        next: (result) => {
          localStorage.setItem('devAuthToken', result.access_token);
          this.loggingIn.set(false);
          this.loggedIn.set(true);
          this.status.set(`Signed in — session expires in ${Math.round(result.expires_in / 60)} minutes.`);
          this.loginSucceeded.emit();
        },
        error: () => {
          this.loggingIn.set(false);
          this.loggedIn.set(false);
          this.status.set('Sign-in failed — check the email and password.');
        },
      });
  }

  protected logOut(): void {
    localStorage.removeItem('devAuthToken');
    this.loggedIn.set(false);
    this.status.set(null);
    this.loggedOut.emit();
  }
}
