import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { HomePage } from './home-page';
import { routes } from '../app.routes';

describe('HomePage', () => {
  let fixture: ComponentFixture<HomePage>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    }).compileComponents();

    fixture = TestBed.createComponent(HomePage);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('mounts the quote browser, both create-a-quote forms, and the demo panel', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-quote-browser')).toBeTruthy();
    expect(compiled.querySelector('app-create-quote-form')).toBeTruthy();
    expect(compiled.querySelector('app-create-quote-form-signals')).toBeTruthy();
    expect(compiled.querySelector('app-http-demo-panel')).toBeTruthy();
  });

  it('forwards a just-created quote into the quote browser', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);

    const quote = { id: 9, ownerId: 'user-9', text: 'Newly created.' };
    fixture.componentInstance['justCreated'].set(quote);
    fixture.detectChanges();

    expect(fixture.componentInstance['justCreated']()).toEqual(quote);
  });

  it('LOGOUT: navigates to /login when DevLogin emits loggedOut', async () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockImplementation(() => Promise.resolve(true));

    fixture.componentInstance['onLoggedOut']();

    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });
});
