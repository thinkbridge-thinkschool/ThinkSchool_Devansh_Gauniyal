import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
      providers: [provideHttpClient(), provideHttpClientTesting()],
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

  it('LOADING: detail loading is true in flight and false after the response settles', () => {
    loadList();
    expect(component.detailLoading()).toBe(false);

    component.selectQuote(1);
    expect(component.detailLoading()).toBe(true);

    httpMock.expectOne('/api/quotes').flush(sample);
    expect(component.detailLoading()).toBe(false);
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

  it('ERROR: a failing detail request sets detailError and leaves detailData unset', () => {
    loadList();
    component.selectQuote(1);
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.detailError()).toBeTruthy();
    expect(component.detailData()).toBeNull();
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

  // --- happy path (non-race), for completeness ---

  it('renders one row per item and lets a selection load its detail', () => {
    loadList();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="quote-list"] li').length).toBe(sample.length);

    component.selectQuote(1);
    httpMock.expectOne('/api/quotes').flush(sample);

    expect(component.detailData()?.id).toBe(1);
  });

  // --- RACE: the core proof ---

  it('RACE: discards a stale detail response when it arrives out of order (select A, select B, flush A last -> pane shows B)', () => {
    loadList();

    component.selectQuote(1); // A: issues detail request #1
    component.selectQuote(2); // B: issues detail request #2 before #1 resolves

    const pending = httpMock.match('/api/quotes');
    expect(pending.length).toBe(2);

    // Flush OUT OF ORDER: B's response resolves first, A's resolves last.
    pending[1].flush(sample); // B (id 2)
    pending[0].flush(sample); // A (id 1), stale by the time it lands

    expect(component.selectedId()).toBe(2);
    expect(component.detailData()?.id).toBe(2);
  });
});
