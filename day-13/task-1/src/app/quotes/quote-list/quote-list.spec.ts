import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteList } from './quote-list';
import { Quote } from '../quote';

describe('QuoteList', () => {
  let component: QuoteList;
  let fixture: ComponentFixture<QuoteList>;
  let httpMock: HttpTestingController;

  const sample: Quote[] = [
    { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
    { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(QuoteList);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushQuotes(data: Quote[] = sample): void {
    fixture.detectChanges();
    httpMock.expectOne('/api/quotes').flush(data);
  }

  it('shows the loading @if branch before the fetch resolves', () => {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="status-loading"]')).toBeTruthy();
    httpMock.expectOne('/api/quotes').flush(sample);
  });

  it('renders the empty @if branch, and not the list, when there are no quotes', () => {
    flushQuotes([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="status-empty"]')).toBeTruthy();
    expect(el.querySelector('.quote-list__items')).toBeFalsy();
  });

  it('renders one row per item when populated', () => {
    flushQuotes(sample);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const items = el.querySelectorAll('.quote-list__items li');
    expect(items.length).toBe(sample.length);
  });

  it('computed updates when the FIRST signal (quotes) changes, filterText held constant', () => {
    flushQuotes(sample);
    expect((component as any).filteredQuotes().length).toBe(2);

    (component as any).quotes.set([sample[0]]);
    expect((component as any).filteredQuotes().length).toBe(1);
  });

  it('computed updates when the SECOND signal (filterText) changes, quotes held constant', () => {
    flushQuotes(sample);
    expect((component as any).filteredQuotes().length).toBe(2);

    (component as any).filterText.set('policies');
    expect((component as any).filteredQuotes().length).toBe(1);
    expect((component as any).filteredQuotes()[0].ownerId).toBe('user-2');
  });

  it('renders the "list" @switch branch by default', () => {
    flushQuotes(sample);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="view-mode-list"]')).toBeTruthy();
  });

  it('renders the "compact" @switch branch', () => {
    flushQuotes(sample);
    (component as any).setViewMode('compact');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="view-mode-compact"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="view-mode-list"]')).toBeFalsy();
  });

  it('renders the "ids-only" @switch branch', () => {
    flushQuotes(sample);
    (component as any).setViewMode('ids-only');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="view-mode-ids-only"]')?.textContent).toContain('#1');
  });
});
