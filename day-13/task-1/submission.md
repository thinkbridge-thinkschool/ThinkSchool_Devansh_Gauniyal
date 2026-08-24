# Day 13 Task 1 — Signals + zoneless + standalone

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-13/task-1/day-13/task-1

## Notes for mentor

### 1. The brief

Build a standalone Angular 21 component against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Use its actual endpoint path and its actual response field names; read the controller and DTOs to get them exactly right, and do not invent or guess a field.

The component must:
- be standalone, with no NgModule anywhere in the project
- hold at least two pieces of state in signal()
- derive at least one value with computed() from BOTH of those signals, so changing either one changes the result
- render the list of quotes with @for, using a track expression keyed on the real identifier field
- use @if to handle the empty and loading states, and @switch for at least one multi-branch case
- obtain every dependency with inject(), never constructor injection
- fetch via a small service that returns the real API shape

Do not add state-management libraries, UI component libraries, or anything beyond what the Angular CLI scaffolds. Zoneless is the Angular 21 default — do not re-add Zone.js and do not call provideZonelessChangeDetection(), which is no longer required.

### 2. The agent's output, verbatim

**`src/app/quotes/quote.ts`**
```typescript
/**
 * Mirrors QuotesApi.Quotes.Quote (day-3/task-3/QuotesApi/Quotes/Quote.cs):
 *   public sealed record Quote(int Id, string OwnerId, string Text);
 * Field names below are camelCase because ASP.NET Core's default Minimal API
 * JSON options use JsonNamingPolicy.CamelCase and Program.cs overrides none of it.
 */
export interface Quote {
  id: number;
  ownerId: string;
  text: string;
}
```

**`src/app/quotes/quote-api.ts`**
```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Quote } from './quote';

// Real contract: GET /api/quotes, mapped from QuotesApi/Program.cs:361-362.
const QUOTES_ENDPOINT = '/api/quotes';

@Injectable({
  providedIn: 'root',
})
export class QuoteApi {
  private readonly http = inject(HttpClient);

  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(QUOTES_ENDPOINT);
  }
}
```

**`src/app/quotes/quote-list/quote-list.ts`**
```typescript
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { QuoteApi } from '../quote-api';
import { Quote } from '../quote';

export type QuoteViewMode = 'list' | 'compact' | 'ids-only';

@Component({
  selector: 'app-quote-list',
  imports: [],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
})
export class QuoteList implements OnInit {
  private readonly api = inject(QuoteApi);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly filterText = signal('');
  protected readonly viewMode = signal<QuoteViewMode>('list');
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly filteredQuotes = computed(() => {
    const term = this.filterText().trim().toLowerCase();
    const all = this.quotes();
    return term === '' ? all : all.filter((quote) => quote.text.toLowerCase().includes(term));
  });

  protected readonly filteredCount = computed(() => this.filteredQuotes().length);

  ngOnInit(): void {
    this.loadQuotes();
  }

  protected setFilterText(value: string): void {
    this.filterText.set(value);
  }

  protected setViewMode(mode: QuoteViewMode): void {
    this.viewMode.set(mode);
  }

  private loadQuotes(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load quotes.');
        this.loading.set(false);
      },
    });
  }
}
```

**`src/app/quotes/quote-list/quote-list.html`**
```html
<section class="quote-list">
  <label class="quote-list__filter">
    Filter
    <input
      type="text"
      data-testid="filter-input"
      [value]="filterText()"
      (input)="setFilterText($any($event.target).value)"
      placeholder="Filter quotes by text…"
    />
  </label>

  <div class="quote-list__view-mode">
    <button type="button" data-testid="view-list" (click)="setViewMode('list')">List</button>
    <button type="button" data-testid="view-compact" (click)="setViewMode('compact')">Compact</button>
    <button type="button" data-testid="view-ids-only" (click)="setViewMode('ids-only')">IDs only</button>
  </div>

  @if (loading()) {
    <p class="quote-list__status" data-testid="status-loading">Loading quotes…</p>
  } @else if (error()) {
    <p class="quote-list__status" data-testid="status-error">{{ error() }}</p>
  } @else if (filteredQuotes().length === 0) {
    <p class="quote-list__status" data-testid="status-empty">No quotes match this filter.</p>
  } @else {
    <p class="quote-list__count" data-testid="status-count">{{ filteredCount() }} quote(s)</p>

    @switch (viewMode()) {
      @case ('list') {
        <ul class="quote-list__items" data-testid="view-mode-list">
          @for (quote of filteredQuotes(); track quote.id) {
            <li>{{ quote.text }} — {{ quote.ownerId }}</li>
          }
        </ul>
      }
      @case ('compact') {
        <ul class="quote-list__items" data-testid="view-mode-compact">
          @for (quote of filteredQuotes(); track quote.id) {
            <li>{{ quote.text }}</li>
          }
        </ul>
      }
      @case ('ids-only') {
        <ul class="quote-list__items" data-testid="view-mode-ids-only">
          @for (quote of filteredQuotes(); track quote.id) {
            <li>#{{ quote.id }}</li>
          }
        </ul>
      }
      @default {
        <p>Unknown view mode.</p>
      }
    }
  }
</section>
```

**`src/app/app.config.ts`**
```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideHttpClient()],
};
```

### 3. The verification log

States and edges exercised, with real test names, run via `npx ng test --watch=false` (13/13 pass, `output/final-test-run.txt`): loading `@if` branch before fetch resolves (`shows the loading @if branch...`); empty `@if` branch with no list rendered (`renders the empty @if branch...`); one row per item when populated (`renders one row per item when populated`); the computed recomputing on the first signal alone and the second signal alone, each with the other held constant (`computed updates when the FIRST signal...` / `...SECOND signal...`); all three `@switch` branches rendering distinctly (`renders the "list"/"compact"/"ids-only" @switch branch`); the service parsing a real-DTO-shaped fixture correctly, and failing to populate `ownerId` from a fixture with a wrong field name (`quote-api.spec.ts`). Structural checks (`node scripts/verify-structural.mjs`, `output/structural-check-final.txt`, 5/5 pass): no `NgModule`, no constructor-parameter injection, every `@for` tracks by `quote.id`, no `Zone.js` reference.

One genuine bug caught and fixed: the first version of the structural check for `@for` track expressions used the regex `/@for\s*\([^)]*track\s+quote\.id[^)]*\)/`. It failed on real output (`FAIL: the @for block has a track expression`) even though the block plainly had one — `[^)]*` broke on the `)` inside `filteredQuotes()`, which appears before `track` in the same expression, so the regex never reached it. Fixed by widening the character class (`.*` instead of `[^)]*`). Two other genuine mistakes (a Jasmine-style `done` callback that doesn't exist on Vitest's `TestContext`, and an assumption that Vitest spec files run in plain Node and can `import 'node:fs'` — they're compiled through the same browser-targeted esbuild pipeline as `ng build`) are logged in full, in order, in `verification-log.md`.

What would break if the API contract changed: if `Quote.OwnerId` were renamed or the JSON casing policy changed, `HttpClient.get<Quote[]>` would not fail to compile or fail at runtime — it doesn't validate the response shape — `ownerId` would just come back `undefined` silently in the browser, exactly as reproduced by the wrong-field-name fixture test.

### Interpretations

- API contract read from `day-3/task-3/QuotesApi/Program.cs:361-362` (route) and `day-3/task-3/QuotesApi/Quotes/Quote.cs` (DTO: `Id`, `OwnerId`, `Text`).
- No live authenticated call was made — no token was generated, requested, or hardcoded; the service is tested with `HttpTestingController` and fixtures built from the real DTO field names.
- Angular CLI pinned to `npm install -g @angular/cli@21` (confirmed `ng version` → `Angular CLI: 21.2.21`) because a plain global install currently resolves to Angular 22, which changes defaults (e.g. default `OnPush`) the task didn't ask for.
- Test runner: Vitest (`@angular/build:unit-test`, `vitest@^4.0.8`), the Angular 21 CLI default — nothing extra was added.
- Two-signals-into-one-computed: `quotes` (fetched list) and `filterText` (user search string) both feed `filteredQuotes = computed(...)`; a third signal, `viewMode`, drives `@switch` only.
- All field names (`id`, `ownerId`, `text`) are taken verbatim from the real `Quote` DTO, camelCased per ASP.NET Core's default Minimal API JSON naming policy (no custom `JsonSerializerOptions` found in `Program.cs`).

## What did you learn this session?

`@for` makes `track` mandatory, so the realistic mistake is tracking the wrong field, not omitting it — proven by my mutation check, which also exposed a gap in my own structural check.

## What would break this?

A renamed field in the real DTO breaks the binding silently with no compile error. Mutating state outside a `signal()` also stops repainting anything now that Zone.js is gone.
