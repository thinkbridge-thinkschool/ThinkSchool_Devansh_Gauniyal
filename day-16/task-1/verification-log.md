# Verification log — day-16/task-1

## Network-tab confirmation (Devansh's own — PENDING)

The build-output proof in `output/lazy-load-proof.md` is the primary evidence per the
task's own instructions. The browser network-tab confirmation below is Devansh's to
perform; it has not been done yet as of this writing, and nothing below should be read
as claiming it has.

**Status: PENDING.**

Script for Devansh to run:
1. `cd day-16/task-1 && npm start` (runs `ng serve`) and open `http://localhost:4200/`
   in a browser.
2. Open DevTools → Network tab, filter to JS, and reload the page. Sign in with the
   dev-login form (any email/password against the real local QuotesApi, or set
   `localStorage.devAuthToken` manually). Note the JS files loaded at this point — the
   main bundle and any initial chunks, but no file whose name matches
   `quote-detail-page` or a similarly-hashed chunk beyond the initial set.
3. Click "Open detail page →" next to any quote in the list.
4. Confirm a NEW JS request appears in the Network tab at exactly that moment — a chunk
   file that was not present in step 2 — and that the detail page renders below the
   existing list.

When Devansh has done this, his literal observation should replace this PENDING block.


Written as work happened, not reconstructed afterwards. Two sections: genuine mistakes
(this one), and the required intentional mutation check (Phase 5, appended later).

## Genuine mistakes and corrections

### 1. Replication copy included files it should have excluded

**What I wrote/ran:**
```
rsync -av --exclude='node_modules' --exclude='dist' --exclude='.angular' \
  --exclude='coverage' --exclude='.git' \
  /Users/devansh/thinkschool/day-15/task-1/ /Users/devansh/thinkschool/day-16/task-1/
```

**How the problem surfaced:** No error — the command succeeded and looked fine. I only
caught it by re-reading the task brief's exclusion list right after running it and then
listing the copied top level with `ls`, which showed an `output/` directory and five
`.md` files (`brief.md`, `PROVENANCE.md`, `README.md`, `submission.md`,
`verification-log.md`) that were day-15/task-1's own task documents, not "the app".

**Real output that showed it:**
```
/Users/devansh/thinkschool/day-16/task-1/output/... (day-15's old dotnet-baseline, etc.)
/Users/devansh/thinkschool/day-16/task-1/PROVENANCE.md
/Users/devansh/thinkschool/day-16/task-1/README.md
/Users/devansh/thinkschool/day-16/task-1/brief.md
/Users/devansh/thinkschool/day-16/task-1/submission.md
/Users/devansh/thinkschool/day-16/task-1/verification-log.md
```

**What I changed:** The task explicitly says to exclude `output`; I had left it off the
`--exclude` list. I also removed day-15's task-specific narrative docs (brief.md,
PROVENANCE.md, README.md, submission.md, verification-log.md) — those describe day-15's
task and would be misleading if left in place under day-16's own commit; day-16 needs its
own versions of each, authored fresh. Ran:
```
rm -rf /Users/devansh/thinkschool/day-16/task-1/output
rm -f /Users/devansh/thinkschool/day-16/task-1/{PROVENANCE,README,brief,submission,verification-log}.md
```
Confirmed with a top-level listing afterward that only source, config, package.json and
package-lock.json remained. No source or test file was affected by this mistake — it was
caught before `npm install` or any build ran, so it did not contaminate anything
downstream.

### 2. Test harness missing `withComponentInputBinding()` masked every route-param test behind "missing"

**What I wrote:** `src/app/quotes/quote-detail-page/quote-detail-page.spec.ts` configured
its `TestBed` with `provideRouter(routes)` — the same `routes` array the real app uses,
but without the `withComponentInputBinding()` extension that `app.config.ts` actually
registers alongside it.

**How the problem surfaced:** Running `npm test` after writing all six tests in that file.
Four of six failed. The `GUARD PASS`, `PARAM WELL-FORMED BUT NOT FOUND`, and
`VIEW TRANSITION FALLBACK` tests all failed with the same real error —
`Error: Expected one matching request for criteria "Match URL: /api/quotes", found
none.` — meaning `QuoteDetailPage`'s effect never issued the HTTP call at all. The
`PARAM MALFORMED` test failed differently — `AssertionError: expected null to be
truthy` on `el.querySelector('[data-testid="detail-page-status-malformed"]')` — the
malformed-state element was simply never rendered, for a URL (`/quotes/abc`) that
should have produced it. Only the `PARAM MISSING` test (navigating to the paramless
`/quotes` route) passed, which was the tell: every case was behaving as if the id were
absent, regardless of the real URL segment.

**Root cause:** without `withComponentInputBinding()`, Angular's router never binds
`:id` to `QuoteDetailPage.id`, so the input stayed `undefined` in every test
regardless of the navigated URL. `undefined` is exactly what
`quote-detail-page.ts`'s `paramProblem` computed signal treats as `'missing'`, so
every test silently collapsed onto the "missing id" branch — a bug that would have
been invisible without the four independent, real assertion failures above, since the
component's actual malformed/not-found/valid logic was never being exercised.

**What I changed:** added `withComponentInputBinding()` to the test file's
`provideRouter(routes, withComponentInputBinding())` call, matching the real
registration in `app.config.ts`. Re-ran the suite: all 15 files / 89 tests passed,
including all four previously-failing tests, now exercising the real `:id` param
(`1`, `abc`, `9999`) instead of always falling through to "missing".

This is the bug cited in submission.md's "ONE genuine bug or wrong assumption" — it
directly involves the real route param (`:id` bound to `Quote.Id`,
`day-3/task-3/QuotesApi/Quotes/Quote.cs`) and the app's real `provideRouter()`
registration in `src/app/app.config.ts`.

## Mutation check (Phase 5) — intentional, kept separate from the genuine mistakes above

Real output only; both mutations were applied, run, and reverted in the working tree.
Full captured output: `output/mutation-A-broken.txt`, `output/mutation-A-reverted.txt`,
`output/mutation-B-broken.txt`, `output/mutation-B-reverted.txt`.

**Mutation A — guard proof.** Changed `src/app/auth/auth.guard.ts`'s body to
unconditionally `return true;`, deleting the real token check. Ran `npm test`. Real
failure, `output/mutation-A-broken.txt`:
```
FAIL src/app/auth/auth.guard.spec.ts > authGuard > UNAUTHENTICATED: returns a UrlTree
     redirecting to "/" (not a boolean) when no token is present
FAIL src/app/quotes/quote-detail-page/quote-detail-page.spec.ts > ... > GUARD REDIRECT:
     an unauthenticated navigation to /quotes/:id is redirected to "/", not left on a
     blank detail route
```
2 test files / 6 tests failed (of 15 files / 89 tests). Reverted with `cp` from a
pre-mutation backup, confirmed byte-identical with `diff`, re-ran: 15 files / 89 tests
passed again (`output/mutation-A-reverted.txt`).

**Mutation B — eager-import proof.** Added `import { QuoteDetailPage } from
'../quote-detail-page/quote-detail-page'; console.log(QuoteDetailPage);` to
`src/app/quotes/quote-browser/quote-browser.ts` (outside the detail page's own
directory). Ran `node scripts/verify-structural.mjs` (the project's own established
tool for static source-text checks — see that script's own header comment on why these
checks live outside `npm test`/Vitest). Real failure, `output/mutation-B-broken.txt`:
```
FAIL: quote-detail-page is never statically imported outside its own directory
      /Users/devansh/thinkschool/day-16/task-1/src/app/quotes/quote-browser/quote-browser.ts
```
Also rebuilt production with the mutation active to confirm it was not just a static
check false-negative-catcher: the lazy chunk shrank to a 71-byte stub and `grep -l
"Quote detail" dist/app/browser/*.js` found the string in the INITIAL chunk
(`chunk-WS3W2K4L.js`), not the lazy one — the real bug this task's LAZY-LOADING PROOF
section is hunting for, reproduced on purpose and confirmed by both the structural
check and the build output. Reverted with `cp` from a pre-mutation backup, confirmed
byte-identical with `diff`, re-ran: all structural checks passed again
(`output/mutation-B-reverted.txt`), and the production rebuild returned to the same
chunk hashes as before the mutation (`chunk-VE5SX3A7.js` / `main-WRLQ7LK7.js` /
`chunk-5PWGBFH4.js`), confirming the build is deterministic and nothing was left
behind.
