import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuotesStore } from './quotes-store';
import { Quote } from './quote';

const sample: Quote[] = [
  { id: 1, ownerId: 'user-1', text: 'Security is a process.' },
  { id: 2, ownerId: 'user-2', text: 'Policies make intent explicit.', author: 'Marcus Aurelius' },
];

describe('QuotesStore', () => {
  let store: QuotesStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(QuotesStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // --- LOADING, list and detail ---------------------------------------------

  it('LOADING (list): listLoading is true while loadQuotes() is in flight and false after it settles', () => {
    expect(store.listLoading()).toBe(true); // initial value, before any load
    store.loadQuotes();
    expect(store.listLoading()).toBe(true);
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(store.listLoading()).toBe(false);
  });

  it('LOADING (detail): detailLoading is true while selectQuote() is in flight and false after it settles', () => {
    expect(store.detailLoading()).toBe(false); // initial value, before any selection
    store.selectQuote(1);
    expect(store.detailLoading()).toBe(true);
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(store.detailLoading()).toBe(false);
  });

  // --- ERROR ------------------------------------------------------------------

  it('ERROR (list): a failing loadQuotes() sets listError and leaves quotes() unset, not stale data presented as fresh', () => {
    store.loadQuotes();
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(store.listError()).toBeTruthy();
    expect(store.quotes()).toBeNull();
    expect(store.listLoading()).toBe(false);
  });

  it('ERROR (detail): a failing selectQuote() sets detailError and leaves selectedQuote() unset', () => {
    store.selectQuote(1);
    httpMock.expectOne('/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(store.detailError()).toBeTruthy();
    expect(store.selectedQuote()).toBeNull();
    expect(store.detailLoading()).toBe(false);
  });

  // --- EMPTY --------------------------------------------------------------------

  it('EMPTY: a successful zero-item load produces isEmpty()=true, distinguishable from the error state', () => {
    store.loadQuotes();
    httpMock.expectOne('/api/quotes').flush([]);

    expect(store.isEmpty()).toBe(true);
    expect(store.listError()).toBeNull();
    expect(store.quotes()).toEqual([]);
  });

  it('EMPTY: is false before any load resolves (quotes() is still null, not an empty array)', () => {
    expect(store.isEmpty()).toBe(false);
    expect(store.quotes()).toBeNull();
  });

  // --- CONCURRENT UPDATES -- the edge the exercise names --------------------

  it('CONCURRENT (detail): an older selectQuote() response arriving last must not overwrite the newer selection', () => {
    store.selectQuote(1); // A: issues detail request #1
    store.selectQuote(2); // B: issues detail request #2 before #1 resolves

    const pending = httpMock.match('/api/quotes');
    expect(pending.length).toBe(2);

    // Flush OUT OF ORDER: B (the newer call) resolves first, A (the older, stale one)
    // resolves last.
    pending[1].flush(sample); // B, selecting id 2
    pending[0].flush(sample); // A, selecting id 1 -- stale by the time it lands

    expect(store.selectedQuote()?.id).toBe(2);
  });

  it('CONCURRENT (list): an older loadQuotes() response arriving last must not leave the older result in place', () => {
    const first: Quote[] = [{ id: 1, ownerId: 'user-1', text: 'First load.' }];
    const second: Quote[] = [
      { id: 1, ownerId: 'user-1', text: 'First load.' },
      { id: 2, ownerId: 'user-2', text: 'Second, newer load.' },
    ];

    store.loadQuotes(); // A
    store.loadQuotes(); // B, before A resolves

    const pending = httpMock.match('/api/quotes');
    expect(pending.length).toBe(2);

    // Flush OUT OF ORDER: B (newer) resolves first, A (older, stale) resolves last.
    pending[1].flush(second); // B
    pending[0].flush(first); // A -- stale by the time it lands

    expect(store.quotes()).toEqual(second);
  });

  // --- STRUCTURAL: read-only exposure (interpretation 4) -----------------------

  it('STRUCTURAL: none of the publicly exposed signals have a reachable set or update method', () => {
    const exposed = [
      store.quotes,
      store.listLoading,
      store.listError,
      store.selectedQuote,
      store.detailLoading,
      store.detailError,
      store.quoteCount,
      store.isEmpty,
    ];
    for (const signalRef of exposed) {
      const asWritable = signalRef as unknown as { set?: unknown; update?: unknown };
      expect(asWritable.set).toBeUndefined();
      expect(asWritable.update).toBeUndefined();
    }
  });

  // --- STRUCTURAL: derived value is computed, not stored twice (interpretation 5) --

  it('STRUCTURAL: quoteCount is a computed derived from quotes -- changing the source changes the derived value', () => {
    expect(store.quoteCount()).toBe(0);
    store.loadQuotes();
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(store.quoteCount()).toBe(sample.length);

    store.addQuote({ id: 3, ownerId: 'user-3', text: 'A third quote.' });
    expect(store.quoteCount()).toBe(sample.length + 1);
  });

  // --- STRUCTURAL: array replaced, not mutated (interpretation 7) -----------------

  it('STRUCTURAL: adding an item through the store notifies a computed derived from the collection, proving the array was replaced', () => {
    store.loadQuotes();
    httpMock.expectOne('/api/quotes').flush(sample);

    const countsSeen: number[] = [];
    // A fresh computed reading store.quotes() -- if addQuote() mutated the existing
    // array in place instead of replacing it, this computed would never re-evaluate,
    // since a signal only notifies subscribers on reassignment, not on a mutation of
    // the value it already holds.
    const derivedCount = TestBed.runInInjectionContext(() => computed(() => store.quotes()?.length ?? 0));
    countsSeen.push(derivedCount());

    store.addQuote({ id: 4, ownerId: 'user-4', text: 'Notifies the computed.' });
    countsSeen.push(derivedCount());

    expect(countsSeen).toEqual([sample.length, sample.length + 1]);
  });

  it('addQuote does not duplicate a quote whose id is already present', () => {
    store.loadQuotes();
    httpMock.expectOne('/api/quotes').flush(sample);

    store.addQuote(sample[0]);

    expect(store.quoteCount()).toBe(sample.length);
  });

  // --- clearSelection: the missing/malformed route-param path ----------------------

  it('clearSelection resets the selection and supersedes an in-flight selectQuote() request', () => {
    store.selectQuote(1);
    expect(store.detailLoading()).toBe(true);

    store.clearSelection();
    expect(store.selectedQuote()).toBeNull();
    expect(store.detailLoading()).toBe(false);
    expect(store.detailError()).toBeNull();

    // The request selectQuote(1) already fired is still pending; flushing it now must
    // not repopulate the state clearSelection() just cleared.
    httpMock.expectOne('/api/quotes').flush(sample);
    expect(store.selectedQuote()).toBeNull();
  });
});
