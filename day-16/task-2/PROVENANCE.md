# Provenance — day-16/task-2

**Source:** `day-16/task-1` at commit `093803ce4c827f45a66902bccd289421f33a0ab1` (`HEAD`
of `day-16/task-1` at the time this branch was cut). Verified as the latest complete
app in Phase 1 — it carries Day 13 through Day 15 plus routing, the lazy-loaded detail
route, the auth guard/guest-only guard, the login/home page split, and the view
transition.

**Copy method:** `rsync -av --exclude='node_modules' --exclude='dist'
--exclude='.angular' --exclude='coverage' --exclude='.git' --exclude='output'
day-16/task-1/ day-16/task-2/`, run once, followed by removing day-16/task-1's own
task-narrative docs (`brief.md`, `PROVENANCE.md`, `README.md`, `submission.md`,
`verification-log.md` — day-16/task-1's versions, not carried forward since day-16/task-2
needs its own) and `npm install` inside `day-16/task-2`.

## The "before" picture (Phase 1, state layout prior to this task)

Before this task, quotes-feature state was duplicated across three places, each
independently calling `QuoteApi` and independently guarding (or not) against stale
responses:

- **`QuoteBrowser`** (`quote-browser.ts`): `listData = signal<Quote[] | null>(null)`,
  `listLoading = signal(true)`, `listError = signal<string | null>(null)`. Called
  `QuoteApi.getQuotes()` directly in `loadList()`. No concurrency guard on this path.
- **`QuoteDetailPage`** (`quote-detail-page.ts`): `loading = signal(false)`,
  `quote = signal<Quote | null>(null)`, `errorMessage = signal<string | null>(null)`.
  Called `QuoteApi.getQuoteDetail()` directly, with its own stale-response guard
  (comparing the requested id against the current route id).
- **`HomePage`** (`home-page.ts`): `justCreated = signal<Quote | null>(null)`, a bridge
  signal that received `CreateQuoteForm`'s/`CreateQuoteFormSignals`'s `quoteCreated`
  output and passed it down into `QuoteBrowser`'s `justCreated` input, which
  `QuoteBrowser` picked up via an `effect()` to splice the new quote into its own list.

No state was shared between `QuoteBrowser` and `QuoteDetailPage` at all — they held two
completely independent copies of "the quotes."

## Files carried unchanged

Every file under `day-16/task-2/src/`, `public/`, config files (`angular.json`,
`tsconfig*.json`, `.editorconfig`, `.prettierrc`, `.gitignore`, `.vscode/`,
`proxy.conf.json`), `package.json` and `package-lock.json` — **except** the files
listed as modified below, and the files newly added, listed further below.

## Files carried and modified, each with its justification

1. **`src/app/quotes/quote-browser/quote-browser.ts`** — removed `listData`,
   `listLoading`, `listError`, the `justCreated` input, and the `syncJustCreated` effect;
   now injects `QuotesStore` and exposes its signals directly, calling
   `store.loadQuotes()` in `ngOnInit()`. Justification: this is precisely the state the
   brief requires moved into the store.

2. **`src/app/quotes/quote-browser/quote-browser.html`** — `listData()` → `quotes()`;
   the inline `listData() && listData()!.length === 0` empty check → `isEmpty()` (the
   store's computed derived value). Justification: template must read the renamed/moved
   signals; `isEmpty()` replaces hand-written empty-check logic with the store's single
   source of truth for it.

3. **`src/app/quotes/quote-browser/quote-browser.spec.ts`** — rewritten to read state
   through an injected `QuotesStore` instead of component-owned signals that no longer
   exist (`component.listLoading()` → `store.listLoading()`, etc.); the `JUST CREATED`
   test (which drove `QuoteBrowser`'s now-removed `justCreated` input) is replaced by a
   `STORE-BACKED` test calling `store.addQuote()` and asserting the same DOM outcome.
   Every other assertion (LOADING, ERROR ×2, EMPTY, the routerLink test) is unchanged in
   substance — same conditions exercised, same DOM assertions, only the plumbing to
   drive/read state changed. Interpretation 9.

4. **`src/app/quotes/quote-detail-page/quote-detail-page.ts`** — removed `loading`,
   `quote`, `errorMessage` signals and the API-calling `effect()`; now injects
   `QuotesStore`, aliases `loading`/`quote`/`errorMessage` directly to the store's
   `detailLoading`/`selectedQuote`/`detailError`, and the effect now calls
   `store.selectQuote(id)` / `store.clearSelection()` instead of calling `QuoteApi`
   itself. Route-param parsing (`id`, `paramProblem`, `numericId`) is untouched — it is
   a routing concern local to this page, not shared feature state. Its own `.spec.ts`
   needed no changes; the same property names (`loading`/`quote`/`errorMessage`) still
   exist with the same call signature, just backed by the store.

5. **`src/app/home-page/home-page.ts`** — removed the `justCreated` bridge signal and
   its `Quote` type import. Justification: this signal WAS a duplicate copy of state
   that now lives only in `QuotesStore`; removing it is exactly what "read from the
   store instead of holding their own copies of this state" requires, not scope creep.

6. **`src/app/home-page/home-page.html`** — removed `(quoteCreated)="justCreated.set($event)"`
   from both form bindings and `[justCreated]="justCreated()"` from `<app-quote-browser>`
   (that input no longer exists). Justification: dead wiring for a bridge that no longer
   exists on either end.

7. **`src/app/home-page/home-page.spec.ts`** — replaced the "forwards a just-created
   quote into the quote browser" test (which drove and read the now-removed
   `justCreated` signal via bracket-notation access) with a `STORE-BACKED` test: calling
   `store.addQuote()` and asserting the mounted `QuoteBrowser`'s rendered list reflects
   it. This proves the same integration property the original test proved — a created
   quote reaches the rendered list without a second HTTP round trip — through the real
   mechanism that now provides it. Interpretation 9.

8. **`src/app/quotes/create-quote-form/create-quote-form.ts`** and
   **`src/app/quotes/create-quote-form-signals/create-quote-form-signals.ts`** — each
   now injects `QuotesStore` and calls `store.addQuote(quote)` in its success handler,
   alongside the existing `quoteCreated.emit(quote)` (kept, unchanged — see below).
   Justification: this is "the create flow's effect on the list" the brief explicitly
   scopes into the store (interpretation 2); the actual `POST /api/quotes` call stays
   exactly where it was, via `QuoteApi.createQuote()`, since that call itself was never
   duplicated state and is out of scope for a state-consolidation task.

9. **`scripts/verify-structural.mjs`** — extended the constructor-injection check to
   also cover `@Injectable(` (this task's requirement is "no component or service",
   day-16/task-1's was "no component, interceptor or guard" — service is new); added
   four new checks: no state-management package in `package.json`, `QuoteBrowser`/
   `QuoteDetailPage` no longer reference `HttpClient` or `QuoteApi`, and
   `quoteCount`/`isEmpty` are `computed(...)` and never separately assigned. See
   verification-log.md for a real bug caught and fixed while adding these.

## What was deliberately NOT changed

`CreateQuoteForm.quoteCreated` and `CreateQuoteFormSignals.quoteCreated` (`output<Quote>`)
are unchanged and still emit on success — kept specifically so their existing,
carried tests (`create-quote-form.spec.ts`'s "emits quoteCreated with the real created
quote on success" and the parity tests) needed zero modification. Nothing in the app
listens to them anymore (see item 6 above), which is harmless: an unlistened `output()`
is inert, not a source of duplicated state, since the list itself is now read from
`QuotesStore` regardless of whether this event is ever bound to anything.

## Files newly added

- `src/app/quotes/quotes-store.ts` — the store service.
- `src/app/quotes/quotes-store.spec.ts` — LOADING (list + detail), ERROR (list +
  detail), EMPTY (×2), CONCURRENT UPDATES (list + detail, out-of-order flushes),
  structural (read-only exposure, computed-not-stored, array-replaced-not-mutated),
  `addQuote` de-duplication, `clearSelection`.
- `brief.md`, `PROVENANCE.md` (this file), `README.md`, `state-threshold.md`,
  `submission.md`, `verification-log.md` — this task's own documents.
- `output/` — this task's own captured build/test evidence.

## Contract discovery this design depends on

- List/create endpoint: `GET /api/quotes` (Program.cs:361-362, no auth) and
  `POST /api/quotes` (Program.cs:364-376, `RequireAuthorization(CanEditQuotes)`) —
  `day-3/task-3/QuotesApi/Program.cs`.
- **No per-item GET/detail endpoint exists.** Only `PUT`/`DELETE /api/quotes/{id:int}`
  (Program.cs:378-413) use the id param, both auth-gated mutations. Re-verified directly
  against Program.cs in this task's own Phase 1, agreeing exactly with what
  `quote-api.ts`'s existing comment and day-16/task-1's `app.routes.ts` already
  documented.
- Response DTO: `Quote(int Id, string OwnerId, string Text, string? Author = null)` —
  `day-3/task-3/QuotesApi/Quotes/Quote.cs:5` — wire shape `{ id, ownerId, text, author?
  }`, camelCase. `quotes-store.ts` holds exactly this shape (`Quote[] | null` and
  `Quote | null`), never a re-derived or renamed field.
- Request DTO: `CreateQuoteRequest(string Text, string? Author = null)` —
  `day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs:8`. Unaffected by this task —
  `addQuote()` receives an already-created `Quote`, it does not construct requests.
- Id field: `Quote.Id`, `int`, non-negative (`InMemoryQuoteRepository.cs`'s `_nextId`).
  Because there is no real detail endpoint, `selectQuote(id)` works exactly as
  `quote-api.ts`'s `getQuoteDetail()` already did: re-call the one real list endpoint
  and resolve the id client-side.
