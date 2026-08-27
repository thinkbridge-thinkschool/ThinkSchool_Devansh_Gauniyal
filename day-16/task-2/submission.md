# Day 16 Task 2 — State management, signals first

This replicates day-16/task-1 at commit `093803ce4c827f45a66902bccd289421f33a0ab1` and
consolidates the quotes feature's state into a signal store; earlier folders are
unchanged.

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-16/task-2/day-16/task-2

## Notes for mentor

### 1. The brief

Take the Angular 21 app from day-16/task-1 and copy it into day-16/task-2 untouched. Do not modify day-16/task-1 or anything earlier.

Working inside day-16/task-2 only, consolidate the quotes feature's state into a single signal-based store service. The API is the QuotesApi at day-3/task-3/QuotesApi — read its controller and DTOs first and use the ACTUAL routes and the ACTUAL field names with exact casing. Do not invent a field.

The store must:
- be a service holding the feature's state in signals: the quotes collection, the selected quote, a loading flag, and an error value
- expose read-only signals to components — components must not be able to write to the state directly, only call the store's methods
- derive anything derivable with computed rather than storing it twice, so there is one source of truth
- own the API calls for this feature, so components ask the store rather than calling HttpClient themselves
- handle concurrent updates correctly: if two loads or two selections overlap, a stale response must not overwrite newer state. The app already has a stale-response guard for the detail; the store must preserve that behaviour rather than reintroducing the bug.
- keep the existing loading, error and empty states working and observable

Then refactor the existing components to read from the store instead of holding their own copies of this state — but change only what that requires. Do not restructure components beyond moving state access to the store.

Separately, draft a rule for when this feature should move from signals to a signal store or NgRx. Ground it in this app's real numbers and names — how many signals this store holds now, which components read it, what specifically would have to grow for the rule to trigger. Not a generic checklist.

Everything already in the app — the list, the detail, both forms, the interceptors, the routing, the guard, the lazy load, the characterization test — must keep working. Do not degrade any existing test. Change nothing else: no refactors beyond the state move, no renames, no dependency upgrades, no restyling.

### 2. The agent's output

**Store service — `src/app/quotes/quotes-store.ts`**
```ts
/**
 * Single source of truth for the quotes feature's state -- the list QuoteBrowser
 * renders, the selection QuoteDetailPage renders, and how a newly created quote lands
 * back in the list. Consolidates state that used to be duplicated: QuoteBrowser and
 * QuoteDetailPage each held their own copy of this state (their own `Quote[]`/`Quote |
 * null`, their own loading/error signals) and each independently called QuoteApi and
 * guarded against stale responses. See PROVENANCE.md / README.md for the "before"
 * picture this replaces.
 *
 * Routes this store calls (day-3/task-3/QuotesApi/Program.cs):
 *   GET /api/quotes (Program.cs:361-362, no auth) -- loadQuotes() calls it directly;
 *     selectQuote() re-calls the SAME endpoint and resolves the requested id
 *     client-side, since the API has no per-item GET route (confirmed by grepping
 *     Program.cs for `MapGet` -- only PUT/DELETE `/api/quotes/{id:int}` exist, both
 *     auth-gated mutations, not reads).
 *   POST /api/quotes is NOT called from here. Creating a quote stays exactly where it
 *     already was -- CreateQuoteForm / CreateQuoteFormSignals still call
 *     QuoteApi.createQuote() directly -- only the resulting quote's effect on THIS
 *     store's list (addQuote()) is this store's concern, per the brief's "the create
 *     flow's effect on the list".
 *
 * Concurrency guard: both loadQuotes() and selectQuote() use a private, monotonically
 * increasing request token -- one counter per operation. Each call captures the
 * counter's new value as its own token before issuing the HTTP request; when a
 * response arrives, it is applied only if its captured token still equals the
 * counter's CURRENT value, i.e. only if no newer call to the same method has started
 * since. A response whose token has been superseded is silently discarded. This is a
 * generalisation of the id-comparison guard quote-detail-page.ts used before this move
 * (which compared the requested id against the CURRENT id): a token also correctly
 * supersedes a second call for the very same id, which an id-comparison alone could
 * not distinguish. The identical pattern now also covers loadQuotes(), which had no
 * guard at all before this move.
 */
import { Injectable, computed, inject, signal } from '@angular/core';
import { AppHttpError } from '../http/app-http-error';
import { QuoteApi } from './quote-api';
import { Quote } from './quote';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly api = inject(QuoteApi);

  private readonly _quotes = signal<Quote[] | null>(null);
  private readonly _listLoading = signal(true);
  private readonly _listError = signal<string | null>(null);

  private readonly _selectedQuote = signal<Quote | null>(null);
  private readonly _detailLoading = signal(false);
  private readonly _detailError = signal<string | null>(null);

  // Read-only views: components can read these but cannot call .set()/.update() on
  // them -- asReadonly() returns a WritableSignal-free Signal, so there is no
  // mutating method on the type components are handed at all, not merely a
  // convention against using one.
  readonly quotes = this._quotes.asReadonly();
  readonly listLoading = this._listLoading.asReadonly();
  readonly listError = this._listError.asReadonly();

  readonly selectedQuote = this._selectedQuote.asReadonly();
  readonly detailLoading = this._detailLoading.asReadonly();
  readonly detailError = this._detailError.asReadonly();

  // Derived from `quotes` -- never assigned directly, anywhere in this file, so they
  // can never drift out of sync with the collection they are computed from.
  readonly quoteCount = computed(() => this._quotes()?.length ?? 0);
  readonly isEmpty = computed(() => this._quotes() !== null && this._quotes()!.length === 0);

  private listRequestToken = 0;
  private detailRequestToken = 0;

  loadQuotes(): void {
    const token = ++this.listRequestToken;
    this._listLoading.set(true);
    this._listError.set(null);
    this.api.getQuotes().subscribe({
      next: (quotes) => {
        if (token !== this.listRequestToken) {
          return; // superseded by a newer loadQuotes() call -- discard, do not apply
        }
        this._quotes.set(quotes);
        this._listLoading.set(false);
      },
      error: (err: unknown) => {
        if (token !== this.listRequestToken) {
          return;
        }
        this._listError.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quotes.');
        this._listLoading.set(false);
      },
    });
  }

  selectQuote(id: number): void {
    const token = ++this.detailRequestToken;
    this._selectedQuote.set(null);
    this._detailError.set(null);
    this._detailLoading.set(true);
    this.api.getQuoteDetail(id).subscribe({
      next: (found) => {
        if (token !== this.detailRequestToken) {
          return; // superseded by a newer selectQuote() call -- discard, do not apply
        }
        this._selectedQuote.set(found ?? null);
        this._detailLoading.set(false);
      },
      error: (err: unknown) => {
        if (token !== this.detailRequestToken) {
          return;
        }
        this._detailError.set(err instanceof AppHttpError ? err.friendlyMessage : 'Failed to load quote detail.');
        this._detailLoading.set(false);
      },
    });
  }

  // Clears the selection without issuing a request -- used when a route param is
  // absent or malformed (see quote-detail-page.ts) so a stale selection from a
  // previous valid id does not linger. Also bumps the token, so a detail request
  // already in flight from a prior selectQuote() call is superseded and its response
  // (whenever it lands) is discarded rather than repopulating the just-cleared state.
  clearSelection(): void {
    this.detailRequestToken++;
    this._selectedQuote.set(null);
    this._detailLoading.set(false);
    this._detailError.set(null);
  }

  // The create flow's effect on the list (see CreateQuoteForm / CreateQuoteFormSignals,
  // both of which call this after a successful POST): appends an already-created quote
  // to the collection. Always REPLACES the array (spreads into a new one) rather than
  // mutating the existing array in place -- a signal only notifies subscribers on
  // reassignment; push()ing into an array a signal already holds changes the array's
  // contents without changing which array the signal holds, so nothing would react.
  addQuote(quote: Quote): void {
    const current = this._quotes() ?? [];
    if (current.some((existing) => existing.id === quote.id)) {
      return;
    }
    this._quotes.set([quote, ...current]);
  }
}
```

**The NgRx threshold rule — `state-threshold.md`**
```md
# When this app would move from signals to a signal store / NgRx

Grounded in this app's real numbers, not a generic checklist.

## The actual numbers, right now

`QuotesStore` (`src/app/quotes/quotes-store.ts`) holds **6 primitive signals** (3
writable pairs: `_quotes`/`_listLoading`/`_listError` for the list, and
`_selectedQuote`/`_detailLoading`/`_detailError` for the detail selection) plus **2
computed values** (`quoteCount`, `isEmpty`) derived from the first pair. That is the
entire state surface: 8 signals total, all in one file, all for one feature.

**4 components touch it.** `QuoteBrowser` and `QuoteDetailPage` both read from it
(`quotes`/`listLoading`/`listError`/`isEmpty`, and
`selectedQuote`/`detailLoading`/`detailError` respectively) and call one method each
(`loadQuotes()`, `selectQuote()`/`clearSelection()`). `CreateQuoteForm` and
`CreateQuoteFormSignals` only ever call one method, `addQuote()`, and read nothing back
from the store at all. No component anywhere else in the app references `QuotesStore`.

## The rule

I would move off plain signals when at least one of these actually happens here, not
before:

1. **A third consumer needs to read this same state.** Right now exactly two components
   read from the store and two only write to it. If a third page or component needed to
   read `quotes` or `selectedQuote` — say a "recently viewed quotes" widget, or a
   dashboard summarizing quote counts — a plain service is still fine for that; the
   trigger is if that third consumer also needs to react to changes made by the OTHER
   two without me manually wiring more `effect()`s to keep them in sync. That's the
   point a signal store's structured update/selector machinery starts paying for itself
   over hand-rolled signals.
2. **Another feature needs to share or coordinate with this state.** Today `QuotesStore`
   is the only store in the app and nothing outside `src/app/quotes/` touches it. The
   day a second feature (say, a "my drafts" or "favorites" feature) needs to read or
   react to the quotes list — not just call its own separate API — is the day cross-
   feature effects and selectors stop being a "nice to have" and start being where bugs
   actually live if hand-rolled.
3. **I need time-travel debugging or serialized action history.** With 6 signals and 4
   consumers, I can reason about every state change by reading `quotes-store.ts` top to
   bottom. NgRx's dev-tools time-travel and action log earn their overhead once the
   number of state-mutating call sites gets large enough that "what changed and why" is
   no longer answerable by reading one file.
4. **Team size crosses a point where convention needs enforcing, not just documenting.**
   Right now I am the only one touching this code, and the read-only-signal /
   computed-not-stored rules are enforced by a couple of structural tests
   (`quotes-store.spec.ts`'s "STRUCTURAL" tests) plus a header comment. That works at
   this scale. It stops working once enough people are adding state that "please only
   mutate through the store's methods" needs to be a compiler-enforced pattern (NgRx's
   reducer/action shape, or `@ngrx/signals`' `patchState`) rather than a convention two
   tests happen to catch.

None of these are true today. `QuotesStore` at 6 signals, 2 readers, 2 writers, one
feature, one contributor is exactly the "small feature" scale the brief says signals are
for. I'd revisit this the day any ONE of the four conditions above stops being
hypothetical — not on a fixed signal count, and not because NgRx is the more
sophisticated-sounding choice.
```

### 3. The verification log

**Real contract, cited from source.** List/create: `GET /api/quotes` and
`POST /api/quotes` (`day-3/task-3/QuotesApi/Program.cs:361-376`). No per-item detail
endpoint exists — only auth-gated `PUT`/`DELETE /api/quotes/{id:int}` mutations
(Program.cs:378-413). Response DTO fields (`id`, `ownerId`, `text`, `author?`) trace to
`Quote(int Id, string OwnerId, string Text, string? Author = null)` —
`day-3/task-3/QuotesApi/Quotes/Quote.cs:5`. `quotes-store.ts` holds exactly this shape.

**States and edges exercised, real test names.** `quotes-store.spec.ts` (13 tests):
`LOADING (list)`, `LOADING (detail)` — both pass; `ERROR (list)`, `ERROR (detail)` —
both pass; `EMPTY` ×2 — both pass; `CONCURRENT (detail)` and `CONCURRENT (list)` — both
pass, each flushing the OLDER of two overlapping requests LAST and asserting the NEWER
result is what the store holds (e.g. selecting quote 1 then quote 2 before either
resolves, flushing quote 2's response first and quote 1's last, asserts
`store.selectedQuote()?.id === 2`); plus the four STRUCTURAL tests (read-only exposure,
computed-not-stored, array-replaced-not-mutated, `addQuote` de-duplication) and
`clearSelection`. All 19 test files / 107 tests pass (`output/final-test-run.txt`).

**Carried tests.** All 94 carried tests still pass, unchanged in substance. Three files
needed adjustment: `quote-browser.spec.ts` and `home-page.spec.ts` (both drove/read a
now-removed bridge signal — `justCreated` — replaced with equivalent `STORE-BACKED`
assertions against the real store, same DOM outcome proven); `quote-detail-page.spec.ts`
needed zero changes (its `loading`/`quote`/`errorMessage` property names stayed the
same, now backed by the store). Full justification for each in `PROVENANCE.md`. Day 13's
forms, the Day 15 interceptors and characterization test, and the Day 16 Task 1 routing/
guard/lazy-load tests all still pass, confirmed in the same 107-test run.

**The one genuine bug caught.** Extending `scripts/verify-structural.mjs` with a new
"no state-management package in package.json" check, I referenced the file's existing
`packageJson` constant — but that `const` is declared further down the file (for the
pre-existing Zone.js checks), and my new check ran before it. Real error:
`ReferenceError: Cannot access 'packageJson' before initialization` at
`verify-structural.mjs:103`. Fixed by reading `package.json` again locally in the new
check instead of depending on a later declaration's position in the file. This is a bug
in the verification tooling itself, not the app; no other correction of substance was
needed while building the store or moving the four components onto it — the full
migration compiled and passed (19 files / 107 tests) the first real run after that fix.

**Mutation check.** Removing the concurrency guard from `selectQuote()` made 3 tests
genuinely fail, in every case with the OLDER response (`id: 1`, "Security is a
process.") winning over the newer selection (`id: 2`, "Policies make intent explicit.")
— exactly the bug the guard prevents. Mutating `addQuote()` to `unshift` in place
instead of `.set([...])` made the two derived-value structural tests genuinely fail
(`expected 2 to be 4`), plus a real `NG0100` Angular change-detection error as a bonus
symptom. Both reverted and confirmed byte-identical; full output in
`output/mutation-A-*.txt` / `output/mutation-B-*.txt`.

**What breaks if the API contract changes.** If `Quote.Id` stopped being a plain `int`
(e.g. became a GUID), nothing in `quotes-store.ts` itself would break at compile time —
`Quote` is a TypeScript interface, not validated at runtime — but `selectQuote(id:
number)`'s signature and `quote-detail-page.ts`'s route-param parsing (which assumes a
non-negative integer string) would both silently stop matching real ids. If a field were
renamed on the server (e.g. `ownerId` → `owner`), `quotes-store.ts` would keep compiling
and running; the mismatch would only surface at runtime as an `undefined` value wherever
`quote.ownerId` is read in a template, with no build-time warning anywhere.

### Interpretations

- Replicated from day-16/task-1 at commit `093803ce4c827f45a66902bccd289421f33a0ab1`.
- Real endpoints: `GET`/`POST /api/quotes` (`Program.cs:361-376`); real fields `id`,
  `ownerId`, `text`, `author?` (`Quote.cs:5`); no per-item detail endpoint exists.
- Feature modelled: the quotes list, the selected detail, and the create flow's effect
  on the list — the feature already in the app, not an invented one.
- No state library added — `package.json` unchanged; verified by a new structural check.
- Read-only exposure via `.asReadonly()` on every publicly exposed signal.
- Concurrency guard: a monotonically increasing request token per operation (list and
  detail each have their own), generalizing the carried id-comparison guard.
- "Angular MCP server" tag read as a topic label, not a deliverable — no MCP server was
  installed, configured, or used.
- Angular major unchanged at 21; no dependency upgraded.

## What did you learn this session?

I'd been keeping the same list of quotes in two separate places without realizing it — the page showing all the quotes, and the page showing one quote's detail, each fetching and storing its own copy. Pulling that into one shared place didn't just tidy the code, it fixed a real gap: before, only one of those two pages actually protected itself against a slow, outdated network response arriving late and clobbering something newer.

## What would break this?

If someone renames a field the server sends back — say `ownerId` becomes `owner` — nothing would fail when the code is built or even when it first runs; the page would just quietly show a blank where that value used to be, and I'd only find out by actually looking at the page, not from an error message anywhere.
