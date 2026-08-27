/**
 * Routed shell for '/login' (see ../../app.routes.ts). Wraps the existing DevLogin card
 * unchanged and navigates to '/' once sign-in succeeds. guestOnlyGuard keeps an already
 * signed-in visitor from ever seeing this page directly.
 */
import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { DevLogin } from '../dev-login/dev-login';

@Component({
  selector: 'app-login-page',
  imports: [DevLogin],
  templateUrl: './login-page.html',
  styleUrl: './login-page.css',
})
export class LoginPage {
  private readonly router = inject(Router);

  protected onLoginSucceeded(): void {
    this.router.navigateByUrl('/');
  }
}
