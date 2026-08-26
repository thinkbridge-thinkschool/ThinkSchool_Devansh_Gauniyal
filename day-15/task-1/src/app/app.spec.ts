import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    localStorage.clear();
    httpMock.verify();
  });

  it('shows the sign-in gate, not the app, when there is no stored token', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-dev-login')).toBeTruthy();
    expect(compiled.querySelector('app-quote-browser')).toBeFalsy();
    expect(compiled.querySelector('app-create-quote-form')).toBeFalsy();
  });

  it('should create the app and mount the quote browser feature once a token is present', () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);

    expect(fixture.componentInstance).toBeTruthy();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-quote-browser')).toBeTruthy();
  });

  it('also mounts the Day 14 create-a-quote form alongside the browser once a token is present', () => {
    localStorage.setItem('devAuthToken', 'fake-token-for-tests');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-create-quote-form')).toBeTruthy();
  });
});
