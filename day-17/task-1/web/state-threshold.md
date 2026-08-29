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
