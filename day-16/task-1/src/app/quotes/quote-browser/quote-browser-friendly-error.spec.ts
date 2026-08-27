/**
 * Proves Step 4F end to end: wired through the app's REAL interceptor chain (not a
 * bare provideHttpClient() like the carried quote-browser.spec.ts uses), a 4xx from the
 * real QuotesApi shape (empty body, as actually observed -- see
 * output/headers-401-post-unauth.txt) renders as a friendly message in the DOM, not a
 * raw HTTP failure. This is additive -- it does not modify the carried spec file.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { API_INTERCEPTORS } from '../../http/api-interceptors';
import { QuoteBrowser } from './quote-browser';

describe('QuoteBrowser friendly error surfacing (real interceptor chain)', () => {
  let fixture: ComponentFixture<QuoteBrowser>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteBrowser],
      // provideRouter([]) added for the same reason as quote-browser.spec.ts -- see
      // that file's comment.
      providers: [
        provideHttpClient(withInterceptors([...API_INTERCEPTORS])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuoteBrowser);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('a 4xx with the real API\'s empty body renders as a readable message, not a raw HTTP failure', () => {
    fixture.detectChanges();
    // Real observed shape (output/headers-401-post-unauth.txt): 401, empty body.
    httpMock.expectOne('/api/quotes').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('[data-testid="list-status-error"]');
    expect(errorEl).toBeTruthy();
    expect(errorEl!.textContent).toBe('You need to sign in to do that.');
    // Never the raw, unfriendly failure text a bare HttpErrorResponse.message would be.
    expect(errorEl!.textContent).not.toContain('Http failure response');
  });
});
