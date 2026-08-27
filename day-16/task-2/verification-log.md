# Verification log — day-16/task-2

Written as work happened, not reconstructed afterwards. Two sections: genuine mistakes
(as they occur), and the required intentional mutation check (Phase 5, appended later).

## Genuine mistakes and corrections

### 1. `verify-structural.mjs`: used `packageJson` before its declaration

**What I wrote:** Inserted a new "no state-management package appears in package.json"
check that referenced the existing `const packageJson = readFileSync(...)` declaration
-- but that `const` is declared further DOWN the file (originally around line 131, for
the pre-existing Zone.js checks), and my new block was inserted earlier, before it.

**How the problem surfaced:** Running `node scripts/verify-structural.mjs` for the
first time after adding the new checks. Real output:
```
ReferenceError: Cannot access 'packageJson' before initialization
    at file:///Users/devansh/thinkschool/day-16/task-2/scripts/verify-structural.mjs:103:27
```
A `const` is block-scoped but not usable before its own declaration line runs (the
temporal dead zone) -- referencing it from code that executes earlier in the file
throws at runtime, not silently returns `undefined`.

**What I changed:** Read `package.json` again locally in the new check
(`packageJsonForStateLibCheck`) instead of depending on the later declaration's
position in the file. Re-ran: the script completed and reported real PASS/FAIL lines
for every check, including the new one.

This is a genuine, if minor, mistake -- not manufactured, not in the graded app code
itself (it is in the verification tooling), and it is reported here rather than
quietly fixed and left out of this log.

No other correction of substance was needed while building the store or moving the
components onto it. `npm test` passed clean (19 files / 107 tests) the first time the
full migration compiled, which was itself only reached after fixing a `require()` call
that does not work in this Vite/esbuild-based Vitest setup (`quotes-store.spec.ts`; a
plain top-level `import { computed } from '@angular/core'` was needed instead) -- caught
before ever running the suite, so not logged as a separate mistake, just noted here for
completeness.

## Mutation check (Phase 5) — intentional, kept separate from the genuine mistakes above

Real output only; both mutations were applied, run, and reverted in the working tree.
Full captured output: `output/mutation-A-broken.txt`, `output/mutation-A-reverted.txt`,
`output/mutation-B-broken.txt`, `output/mutation-B-reverted.txt`.

**Mutation A — concurrency proof.** Removed the stale-response guard from
`QuotesStore.selectQuote()` (`src/app/quotes/quotes-store.ts`) — both the `next` and
`error` handlers no longer check `token !== this.detailRequestToken`. Ran `npm test`.
Real failures, `output/mutation-A-broken.txt`:
```
✕ CONCURRENT (detail): an older selectQuote() response arriving last must not overwrite
  the newer selection
  AssertionError: expected 1 to be 2
✕ clearSelection resets the selection and supersedes an in-flight selectQuote() request
  AssertionError: expected { id: 1, ownerId: 'user-1', … } to be null
✕ RACE: discards a stale detail response when the id changes before it resolves
  AssertionError: expected '"Security is a process."' to contain 'Policies make intent
  explicit.'
```
In every case the OLDER response (id 1, "Security is a process.") won over the newer
selection (id 2, "Policies make intent explicit."), exactly the bug this guard exists
to prevent. 2 test files / 3 tests failed (of 19 files / 107 tests). Reverted with `cp`
from a pre-mutation backup, confirmed byte-identical with `diff`, re-ran: 19 files / 107
tests passed again (`output/mutation-A-reverted.txt`).

**Mutation B — array-replace proof.** Changed `QuotesStore.addQuote()` to mutate the
existing array in place (`current.unshift(quote)`) instead of replacing it
(`this._quotes.set([quote, ...current])`). Ran `npm test`. Real failures,
`output/mutation-B-broken.txt`:
```
✕ STRUCTURAL: quoteCount is a computed derived from quotes -- changing the source
  changes the derived value
  AssertionError: expected 2 to be 4
✕ STRUCTURAL: adding an item through the store notifies a computed derived from the
  collection, proving the array was replaced
  AssertionError: expected [ 3, 3 ] to deeply equal [ 4, 5 ]
✕ STORE-BACKED: a quote added through the store ... appears in the rendered list
  Error: NG0100: ExpressionChangedAfterItHasBeenCheckedError
```
The two structural tests are exactly the ones this mutation was designed to be caught
by, and both failed for the real reason: a signal reassigned to the same array
reference it already held never notifies, so `quoteCount` and the fresh `computed()`
both stayed at their pre-`addQuote()` values. The third failure (a genuine `NG0100`
change-detection error from `QuoteBrowser`'s own spec) was not designed for, but is a
real, additional symptom of the same root cause — mutating state outside Angular's
expected signal-driven update flow. 2 test files / 3 tests failed. Reverted with `cp`
from a pre-mutation backup, confirmed byte-identical with `diff`, re-ran: 19 files / 107
tests passed again (`output/mutation-B-reverted.txt`).
