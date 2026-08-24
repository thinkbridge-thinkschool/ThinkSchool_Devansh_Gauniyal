# Day 13 Task 2 — A real component from a spec

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-13/task-2/day-13/task-2

## Notes for mentor

### 1. The brief

Build a quotes list-plus-detail component in Angular 21 against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Read its controller and DTOs and use the actual route paths and the actual response field names, exactly as spelled and cased. Do not invent, guess or "tidy" a field name. Name the endpoints and fields you used in a comment at the top of the service.

Requirements:
- standalone components, no NgModule anywhere
- three signals covering loading, error and data, for BOTH the list and the selected detail
- inject() for every dependency, never constructor injection
- the model fully typed against the real API — no `any` anywhere, no `as any`, no implicit any; TypeScript strict mode on
- selecting an item in the list loads its detail; the two requests can interleave, so a response for a selection that is no longer current must be discarded rather than applied
- an error from either request must surface in the error signal — never swallowed, never converted into an empty result
- render with the new control flow, @for with a real track expression keyed on the actual identifier field, and @if for the loading, error and empty states

Do not add state-management libraries, UI component libraries, HTTP wrappers, or anything beyond what the Angular CLI scaffolds. Zoneless is the Angular 21 default — do not re-add Zone.js and do not call provideZonelessChangeDetection().

### 2. The agent's output, verbatim

**`src/app/quotes/quote.ts`**
```typescript
/**
 * Mirrors QuotesApi.Quotes.Quote (day-3/task-3/QuotesApi/Quotes/Quote.cs):
 *   public sealed record Quote(int Id, string OwnerId, string Text);
 * Field names below are camelCase because ASP.NET Core's default Minimal API
 * JSON options use JsonNamingPolicy.CamelCase and Program.cs overrides none of it
 * (confirmed live in Day 13 Task 1 by running the real API unauthenticated).
 */
export interface Quote {
  id: number;
  ownerId: string;
  text: string;
}
```

**`src/app/quotes/quote-api.ts`**
```typescript
/**
 * Endpoints used, read from day-3/task-3/QuotesApi/Program.cs:
 *   GET /api/quotes -> Quote[]  (Program.cs:361-362, no auth required)
 * Fields, read from day-3/task-3/QuotesApi/Quotes/Quote.cs:
 *   public sealed record Quote(int Id, string OwnerId, string Text);
 *   -> wire shape { id, ownerId, text } (camelCase; no JSON naming-policy override
 *      exists in Program.cs, and this was confirmed live in Day 13 Task 1).
 *
 * There is no per-item GET route on this API (day-3/task-3/QuotesApi/Program.cs has
 * no `MapGet` with a route parameter — only PUT/DELETE `/api/quotes/{id:int}`, both
 * auth-gated mutations). getQuoteDetail() below re-calls the one real read endpoint,
 * GET /api/quotes, and resolves the requested item client-side, so the "detail" fetch
 * is still a genuine, independently-timed HTTP round trip rather than a synchronous
 * lookup into the already-loaded list.
 */
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { Quote } from './quote';

const QUOTES_ENDPOINT = '/api/quotes';

@Injectable({
  providedIn: 'root',
})
export class QuoteApi {
  private readonly http = inject(HttpClient);

  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(QUOTES_ENDPOINT);
  }

  getQuoteDetail(id: number): Observable<Quote | undefined> {
    return this.http
      .get<Quote[]>(QUOTES_ENDPOINT)
      .pipe(map((quotes) => quotes.find((quote) => quote.id === id)));
  }
}
```

**`src/app/quotes/quote-browser/quote-browser.ts`**
```typescript
import { Component, OnInit, inject, signal } from '@angular/core';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

@Component({
  selector: 'app-quote-browser',
  imports: [],
  templateUrl: './quote-browser.html',
  styleUrl: './quote-browser.css',
})
export class QuoteBrowser implements OnInit {
  private readonly api = inject(QuoteApi);

  public readonly listData = signal<Quote[] | null>(null);
  public readonly listLoading = signal(true);
  public readonly listError = signal<string | null>(null);

  public readonly selectedId = signal<number | null>(null);
  public readonly detailData = signal<Quote | null>(null);
  public readonly detailLoading = signal(false);
  public readonly detailError = signal<string | null>(null);

  ngOnInit(): void {
    this.loadList();
  }

  private loadList(): void {
    this.listLoading.set(true);
    this.listError.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        this.listData.set(quotes);
        this.listLoading.set(false);
      },
      error: () => {
        this.listError.set('Failed to load quotes.');
        this.listLoading.set(false);
      },
    });
  }

  public selectQuote(id: number): void {
    this.selectedId.set(id);
    this.detailLoading.set(true);
    this.detailError.set(null);
    this.detailData.set(null);

    // Stale-response guard: `id` is captured at request time by this closure. By the
    // time this callback runs, a later call to selectQuote() may already have moved
    // selectedId() on. If so, this response belongs to a selection that is no longer
    // current and must be discarded rather than applied.
    this.api.getQuoteDetail(id).subscribe({
      next: (quote) => {
        if (this.selectedId() !== id) {
          return;
        }
        this.detailData.set(quote ?? null);
        this.detailLoading.set(false);
      },
      error: () => {
        if (this.selectedId() !== id) {
          return;
        }
        this.detailError.set('Failed to load quote detail.');
        this.detailLoading.set(false);
      },
    });
  }
}
```

**`src/app/quotes/quote-browser/quote-browser.html`**
```html
<section class="quote-browser">
  <div class="quote-browser__list" data-testid="list-pane">
    <h2>Quotes</h2>

    @if (listLoading()) {
      <p data-testid="list-status-loading">Loading quotes…</p>
    } @else if (listError()) {
      <p data-testid="list-status-error">{{ listError() }}</p>
    } @else if (listData() && listData()!.length === 0) {
      <p data-testid="list-status-empty">No quotes yet.</p>
    } @else {
      <ul data-testid="quote-list">
        @for (quote of listData(); track quote.id) {
          <li>
            <button
              type="button"
              [attr.data-testid]="'select-' + quote.id"
              [class.selected]="selectedId() === quote.id"
              (click)="selectQuote(quote.id)"
            >
              {{ quote.text }}
            </button>
          </li>
        }
      </ul>
    }
  </div>

  <div class="quote-browser__detail" data-testid="detail-pane">
    <h2>Detail</h2>

    @if (selectedId() === null) {
      <p data-testid="detail-status-unselected">Select a quote to see its detail.</p>
    } @else if (detailLoading()) {
      <p data-testid="detail-status-loading">Loading detail…</p>
    } @else if (detailError()) {
      <p data-testid="detail-status-error">{{ detailError() }}</p>
    } @else if (detailData()) {
      <article data-testid="detail-content">
        <p class="quote-browser__detail-text">“{{ detailData()!.text }}”</p>
        <p class="quote-browser__detail-owner">{{ detailData()!.ownerId }}</p>
      </article>
    } @else {
      <p data-testid="detail-status-not-found">That quote is no longer available.</p>
    }
  </div>
</section>
```

### 3. The verification log

API grounding: `GET /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:361-362`) is the only real read route — there is no `GET /api/quotes/{id}` (only auth-gated `PUT`/`DELETE /api/quotes/{id:int}` take a path parameter). The DTO is `public sealed record Quote(int Id, string OwnerId, string Text)` (`day-3/task-3/QuotesApi/Quotes/Quote.cs`), camelCased on the wire (`id`/`ownerId`/`text`) — confirmed live in Task 1 by running the real API unauthenticated.

States and edges exercised, real test names, `npx ng test --watch=false` → 15/15 pass (`output/final-test-run.txt`): `LOADING: list loading is true in flight and false after the response settles`; `LOADING: detail loading is true in flight and false after the response settles`; `ERROR: a failing list request sets listError and leaves listData unset, not an empty success`; `ERROR: the list error state renders and is distinguishable from the empty state`; `ERROR: a failing detail request sets detailError and leaves detailData unset`; `EMPTY: a successful zero-item response renders the empty branch, not the list or the error branch`; `RACE: discards a stale detail response when it arrives out of order (select A, select B, flush A last -> pane shows B)`. The race test's actual out-of-order flush: select id 1, then id 2 (before id 1's request resolves), flush id 2's request first, flush id 1's request *last* — result: `selectedId()` is `2` and `detailData()?.id` is `2`, i.e. the pane shows B, not the response that arrived last. Structural checks (`node scripts/verify-structural.mjs`) → 7/7 pass, including "no `any` anywhere" and "strict + noImplicitAny enabled".

One genuine bug caught and fixed: the first version of `quote-browser.spec.ts` accessed the component's `protected` state via `(component as any).listLoading()` etc. — 17 occurrences, copied from Day 13 Task 1's pattern, which didn't ban `any` in spec files. This task's brief does ban it everywhere, and `node scripts/verify-structural.mjs` caught it on the very first run:
```
FAIL: no `any` anywhere (`: any`, `as any`, `<any>`, `any[]`)
      .../quote-browser.spec.ts: expect((component as any).listLoading()).toBe(true); | ... [17 occurrences]
```
Fixed by changing the component's state signals and `selectQuote` from `protected` to `public` — a legitimate visibility choice (Angular's template type-checker only requires `protected`/`public`) — so tests call `component.listLoading()` etc. directly with no cast at all. Re-ran: 7/7 structural checks pass, 15/15 tests still pass.

The mutation check separately proved the race guard for real: removing the `if (this.selectedId() !== id) return;` checks made the RACE test fail with `expected 1 to be 2` (pane showed A, not B) — reverted, re-ran green. A second mutation (swallowing the list error into `listData.set([])`) failed both ERROR tests for real — reverted, re-ran green.

What breaks if the Week-1 API contract changes: if `Quote.OwnerId` were renamed, `HttpClient.get<Quote[]>` would not fail to compile — it doesn't validate the response shape — `ownerId` would just come back `undefined` silently in the browser.

### Interpretations

- API read from `day-3/task-3/QuotesApi/Program.cs:361-362` (route) and `day-3/task-3/QuotesApi/Quotes/Quote.cs` (DTO), re-verified against source for this task rather than trusted from Task 1.
- No real detail endpoint exists (only the list route and auth-gated mutations) — `getQuoteDetail()` re-calls `GET /api/quotes` and resolves the item client-side, keeping the async race genuine rather than a synchronous list lookup.
- No live authenticated call was made — no token generated or hardcoded; tested via `HttpTestingController` with fixtures from the real DTO.
- Angular CLI pinned to 21, re-verified (`ng version` → `Angular CLI: 21.2.21`) rather than assumed to still be 21 after Task 1.
- The stale-response guard captures the selected `id` in the closure at request time and discards the response if `selectedId()` has since moved on, before touching any signal.
- `tsconfig.json` has `"strict": true` (which resolves `noImplicitAny: true`, confirmed via `npx tsc --showConfig`); a structural check greps every `.ts` file, including specs, for `any` in any form.
- Test runner: Vitest (`@angular/build:unit-test`), the Angular 21 CLI default — nothing extra added.

## What did you learn this session?

A stricter rule from one task doesn't retroactively apply to a similar-looking task from before — I carried over Task 1's `(component as any)` test pattern without checking that this brief bans `any` everywhere, including specs, and my own structural check caught it immediately.

## What would break this?

A renamed field in the real DTO breaks the binding silently with no compile-time warning, same as Task 1. The race guard only compares against `selectedId()`, so a different kind of interleaving — like the list request itself racing a second list request — would slip past it untouched.
