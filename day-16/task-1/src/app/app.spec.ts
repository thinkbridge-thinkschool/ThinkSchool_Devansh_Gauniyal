import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

// App is now a thin shell (a <router-outlet /> and nothing else) -- the auth-gate /
// home-page behavior this file used to assert on directly now lives at the routes
// themselves (see home-page.spec.ts, login-page.spec.ts, auth.guard.spec.ts). This
// test only confirms the shell itself constructs and renders its outlet.
describe('App', () => {
  it('creates and renders a router-outlet', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.componentInstance).toBeTruthy();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });
});
