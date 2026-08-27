import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { QuoteBrowser } from './quote-browser';
import { Quote } from '../quote';

describe('QuoteBrowser', () => {
  let component: QuoteBrowser;
  let fixture: ComponentFixture<QuoteBrowser>;
  let httpMock: HttpTestingController;

  const sample: Quote[] = [
    { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
    { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteBrowser],
      // provideRouter([]) -- RouterLink injects Router at construction, so it must be
      // resolvable, even though none of the tests below trigger a real navigation.
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(QuoteBrowser);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function loadList(data: Quote[] = sample): void {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush(data);
  }

  // --- LOADING ---

  it('LOADING: list loading is true in flight and false after the response settles', () => {
    fixture.detectChanges();
    expect(component.listLoading()).toBe(true);
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(component.listLoading()).toBe(false);
  });

  // --- ERROR ---

  it('ERROR: a failing list request sets listError and leaves listData unset, not an empty success', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.listError()).toBeTruthy();
    expect(component.listData()).toBeNull();
  });

  it('ERROR: the list error state renders and is distinguishable from the empty state', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="list-status-error"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="list-status-empty"]')).toBeFalsy();
    expect(el.querySelector('[data-testid="quote-list"]')).toBeFalsy();
  });

  // --- EMPTY ---

  it('EMPTY: a successful zero-item response renders the empty branch, not the list or the error branch', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush([]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="list-status-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="quote-list"]')).toBeFalsy();
    expect(el.querySelector('[data-testid="list-status-error"]')).toBeFalsy();
  });

  // --- happy path: each row links to its own routed detail page ---

  it('renders one row per item, each linking straight to its own /quotes/:id detail page', () => {
    loadList();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="quote-list"] li').length).toBe(sample.length);

    const firstLink = el.querySelector('[data-testid="open-detail-1"]') as HTMLAnchorElement;
    const secondLink = el.querySelector('[data-testid="open-detail-2"]') as HTMLAnchorElement;
    expect(firstLink.getAttribute('href')).toBe('/quotes/1');
    expect(secondLink.getAttribute('href')).toBe('/quotes/2');
    expect(firstLink.textContent?.trim()).toBe(sample[0].text);
  });

  // --- Day 14: justCreated, added alongside the existing behaviour above ---

  it('JUST CREATED: prepends a quote passed in via justCreated without duplicating an existing one', async () => {
    loadList();

    fixture.componentRef.setInput('justCreated', { id: 3, ownerId: 'user-3', text: 'Brand new quote.' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.listData()?.length).toBe(sample.length + 1);
    expect(component.listData()?.[0].text).toBe('Brand new quote.');

    // Setting the same quote again (e.g. a stale re-emit) must not duplicate it.
    fixture.componentRef.setInput('justCreated', { id: 3, ownerId: 'user-3', text: 'Brand new quote.' });
    await fixture.whenStable();
    expect(component.listData()?.length).toBe(sample.length + 1);
  });
});
