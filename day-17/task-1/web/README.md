# Day 16 Task 2 — State management, signals first

Replicates `day-16/task-1` (commit `093803ce4c827f45a66902bccd289421f33a0ab1`) unchanged
into this folder, then consolidates the quotes feature's state into one signal-based
store service, `QuotesStore`. See `PROVENANCE.md` for the exact file-by-file diff and
the "before" state layout, and `verification-log.md` for what actually went wrong while
building it.

## What a signal is, and why reassignment matters, not mutation

A signal (`signal(initialValue)`) is a reactive container: anything that reads it inside
a template, a `computed()`, or an `effect()` is automatically re-run when the signal's
value changes — but "changes" specifically means the signal was given a **new** value via
`.set()` or `.update()`, not that something reached inside the value it already holds and
mutated it. If a signal holds an array and code does `array.push(item)` on the array the
signal currently returns, the array's contents changed but the signal itself was never
told anything changed — nothing re-renders, nothing re-computes, the signal is still
holding a reference to the very same (now-mutated) array object it always was. That's why
`QuotesStore.addQuote()` (`quotes-store.ts`) always writes
`this._quotes.set([quote, ...current])` — a brand new array — never `current.push(quote)`.
`quotes-store.spec.ts`'s "STRUCTURAL: adding an item through the store notifies a
computed derived from the collection" test proves this isn't just a style preference: it
creates a separate `computed()` reading `store.quotes()`, calls `addQuote()`, and asserts
that computed actually re-evaluated — which it only can if the signal was reassigned.

## Why derived values must be computed, not stored twice

`quoteCount` and `isEmpty` (`quotes-store.ts`) are both `computed(() => ...)` over
`quotes`, never their own `signal()`. The alternative — a separate `quoteCount =
signal(0)` incremented by hand every time `_quotes` changes — creates two places that
must always agree, and nothing enforces that they do; the moment one code path updates
`_quotes` without remembering to also update `quoteCount`, the two silently drift apart
and the bug is invisible until something reads the stale one. A `computed()` cannot drift
by construction: it has no independent value of its own to fall out of sync with, it is
simply re-evaluated from its real source every time that source changes.
`scripts/verify-structural.mjs`'s "quoteCount and isEmpty are computed over quotes(),
never separately assigned" check backs this up by reading the store's own source text,
not just trusting a test to catch a future regression.

## Why the store exposes read-only signals

`quotes-store.ts` never exposes `_quotes`, `_listLoading`, etc. (the private, writable
signals) directly. Components only ever see the result of `.asReadonly()` — a `Signal<T>`
with no `.set()` or `.update()` on its type at all, not merely a naming convention asking
callers not to use one. A component holding a genuinely writable signal could set it to
anything, from anywhere, entirely bypassing the store's loading/error bookkeeping and its
concurrency guard — the store would no longer be a reliable single source of truth, just
a shared variable. `quotes-store.spec.ts`'s "STRUCTURAL: none of the publicly exposed
signals have a reachable set or update method" test asserts this directly against every
exposed signal's actual shape at runtime.

## How the concurrency guard works

Both `loadQuotes()` and `selectQuote(id)` use a private, monotonically increasing
counter (`listRequestToken` / `detailRequestToken`). Each call increments the counter and
captures that new value as its own token *before* issuing the HTTP request. When a
response arrives, it is only applied if its captured token still equals the counter's
*current* value — i.e. only if no newer call to the same method has started in the
meantime. A response whose token has since been superseded by a newer call is silently
discarded.

This generalizes the guard `quote-detail-page.ts` used before this task (which compared
the *requested id* against the *current* route id): an id comparison cannot distinguish
two separate calls for the exact same id, while a token can — the second call always
supersedes the first even when both ask for id 1. The identical pattern now also covers
`loadQuotes()`, which had no concurrency guard at all before this move (nothing in the
carried app ever called it twice in a row, so the gap was never visible — but the
exercise explicitly asks for it to be tested, so it needed the same real protection).
Proven by `quotes-store.spec.ts`'s two "CONCURRENT" tests, each issuing two overlapping
calls and flushing the *older* one's response *last*, then asserting the newer call's
result is what the store actually holds.

## State layout, before and after

See `PROVENANCE.md`'s "before" section for the full before picture (three independent
copies of quotes state, no shared source of truth, no concurrency guard on the list
path). After this task: `QuotesStore` holds all of it in one place — 6 primitive signals
plus 2 computed values, read by `QuoteBrowser` and `QuoteDetailPage`, written to by those
same two plus `CreateQuoteForm`/`CreateQuoteFormSignals` (via `addQuote()` only, they
never read the store back). Full numbers and the actual NgRx-threshold reasoning grounded
in them are in `state-threshold.md`.

## The ten resolved interpretations, in full

1. **The contract.** Re-verified directly against `day-3/task-3/QuotesApi/Program.cs`
   and `Quotes/Quote.cs` in this task's own Phase 1, agreeing exactly with what the
   carried app already documented. Every field the store holds (`id`, `ownerId`, `text`,
   `author?`) traces to `Quote.cs:5`; every route it calls traces to `Program.cs:361-376`.
2. **Which feature.** The quotes feature already in the app: the list, the selected
   detail, and the create flow's effect on the list — not a new, invented feature.
3. **What "signals first" means and what not to build.** No NgRx, no `@ngrx/signals`, no
   state library of any kind was installed — `package.json` is unchanged apart from
   nothing (verified by `scripts/verify-structural.mjs`'s new state-library check). The
   deliverable is a plain `@Injectable({ providedIn: 'root' })` service holding signals,
   plus the written rule in `state-threshold.md`.
4. **Read-only exposure is structural.** `.asReadonly()` on every exposed signal —
   asserted by test that no `.set`/`.update` is reachable on the type, per interpretation
   4's own wording, not merely documented as a convention.
5. **One source of truth.** `quoteCount`/`isEmpty` are `computed()`, never a second
   `signal()` a caller could update independently — asserted by test AND by static
   source inspection (`verify-structural.mjs`).
6. **Concurrent updates.** The pre-existing detail stale-response guard is preserved
   (generalized to a token, which is strictly stronger — see above) and the list path,
   which had no guard before, now has the identical protection, both proven by
   out-of-order-flush tests.
7. **Signals holding arrays.** `addQuote()` always replaces the array via `.set([...])`,
   never mutates in place — proven by a real notification test (a fresh `computed()`
   over `quotes()` genuinely re-evaluates after `addQuote()`), not merely asserted from
   the source.
8. **The NgRx rule.** Drafted in `state-threshold.md`, grounded in this store's real
   count (6 signals + 2 computed), the real 4 components that touch it, and specific,
   concrete trigger conditions — not a generic checklist.
9. **Regression risk from moving state.** Three carried test files needed adjustment
   (`quote-browser.spec.ts`, `home-page.spec.ts` — both named with justification in
   PROVENANCE.md); `quote-detail-page.spec.ts` needed none, since its property names
   stayed the same. Every carried assertion is unchanged in substance; nothing was
   deleted or weakened to go green.
10. **Evidence.** Every route, field name, signal count, test count, and error message
    quoted anywhere in this README, `PROVENANCE.md`, `submission.md`, and
    `verification-log.md` comes from a real command captured to `output/` or real file
    inspection with a cited path.

## Why no state library was added

The task body is explicit — "signals first; reach for a store only when scale demands"
— and interpretation 3 spells out the consequence directly: installing NgRx or
`@ngrx/signals` here would be misreading the task. This feature is small enough (6
signals, 2 computed values, 4 consuming components, one contributor) that a plain
service with `signal()`/`computed()`/`.asReadonly()` gives every structural guarantee a
heavier store would (single source of truth, read-only exposure, correct concurrency
handling) without the extra dependency, extra boilerplate (actions/reducers/effects), or
extra thing to learn. `state-threshold.md` names the specific, concrete conditions under
which that calculus would flip.

## How to run and test the app

```
cd day-16/task-2
npm install                          # already done; re-run is idempotent
npm test                             # ng test — Vitest, runs once (not watch)
node scripts/verify-structural.mjs   # structural checks, including the new store ones
npm start                            # ng serve, for manual/browser verification
```

No SQL Server, Docker container, or running QuotesApi instance is required for any of
the above — every HTTP interaction in every test is intercepted by
`HttpTestingController`.
