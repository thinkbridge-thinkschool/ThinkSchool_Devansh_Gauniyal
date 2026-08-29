import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { QuoteBrowser } from './quote-browser';
import { QuotesStore } from '../quotes-store';
import { Quote } from '../quote';

// QuoteBrowser no longer owns any state of its own -- it renders whatever
// QuotesStore holds (see ../quotes-store.ts and ../quotes-store.spec.ts, which is
// where the exhaustive loading/error/concurrent-update coverage for the store itself
// now lives). This file keeps the same DOM-level assertions the carried version had,
// but drives and reads them through the injected store instead of component-owned
// signals that no longer exist -- interpretation 9.
describe('QuoteBrowser', () => {
  let fixture: ComponentFixture<QuoteBrowser>;
  let httpMock: HttpTestingController;
  let store: QuotesStore;

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
    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(QuotesStore);
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
    expect(store.listLoading()).toBe(true);
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(store.listLoading()).toBe(false);
  });

  // --- ERROR ---

  it('ERROR: a failing list request sets the store error and leaves the collection unset, not an empty success', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(store.listError()).toBeTruthy();
    expect(store.quotes()).toBeNull();
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

  // --- the create flow's effect on the list, now via the store (was: a `justCreated`
  // input bridged through HomePage; see PROVENANCE.md and quotes-store.spec.ts for the
  // store-level duplicate-prevention coverage) ---

  it('STORE-BACKED: a quote added through the store (as the create flow does on success) appears in the rendered list with no second HTTP call', () => {
    loadList();
    fixture.detectChanges();

    store.addQuote({ id: 3, ownerId: 'user-3', text: 'Brand new quote.' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="quote-list"] li').length).toBe(sample.length + 1);
    expect(el.querySelector('[data-testid="open-detail-3"]')?.textContent?.trim()).toBe('Brand new quote.');
  });
});
