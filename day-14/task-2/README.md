# Day 14 Task 2 — Signal Forms preview

This is NOT a fresh scaffold and NOT an edit to `day-14/task-1`. It is a
copy-forward of `day-14/task-1` at commit
`32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba`, with a Signal Forms version of
the create-a-quote form added **alongside** the existing reactive-forms one.
Both forms are kept, deliberately — the task requires a direct comparison,
and having both in one app against the same API is what makes that
checkable rather than a claim about code in another folder. Built by
directing an agent (Claude Code) per the brief in `brief.md`, then verified
by reading the diff, running it, and mutating it. See `verification-log.md`
for what actually happened while building it, including four genuine
mistakes.

## What was carried vs. added

Full file-by-file accounting in `PROVENANCE.md`. In short: everything from
`day-14/task-1` is carried unchanged — the reactive form, the quotes
browser (list, detail, and the Day 13 stale-response guard), the shared
`QuoteApi` service, the dev-login/sign-in gate. Three carried files were
modified, each with a one-line justification in `PROVENANCE.md`
(`app.ts`/`app.html` to host the new form; `verify-structural.mjs` to check
it). Everything else is new: the `create-quote-form-signals/` component and
its tests, plus a parity spec that instantiates both forms together.

## The real Signal Forms surface

`node_modules/@angular/forms/package.json`'s `exports` map, for the
`@angular/forms@21.2.21` actually installed in this project:

```json
"./signals": {
  "types": "./types/signals.d.ts",
  "default": "./fesm2022/signals.mjs"
}
```

Real import path: **`@angular/forms/signals`**. Every symbol used here was
read directly from `node_modules/@angular/forms/types/signals.d.ts` and
`_structure-chunk.d.ts` (which `signals.d.ts` re-exports most of its surface
from), not assumed:

- `form(modelSignal, schema, options?)` — creates a `FieldTree` wrapping a
  writable model signal.
- `schema<TModel>(fn)` — builds a reusable schema; `fn` receives a path
  proxy (`(path) => { required(path.text, ...); }`).
- `required(path, config?)` — the one validator used here (see below).
- `submit(fieldTree, options?)` — actually posts the form; used implicitly
  via the `FormRoot` directive (`<form [formRoot]="quoteForm">`), which
  prevents the native submit event and calls the framework's own `submit()`
  using the `submission` options passed to `form()` at construction time.
- `FormField` — directive, selector `[formField]`, binds a `FieldTree` to a
  native `<input>`/`<textarea>`.
- Every symbol above carries `@experimental 21.0.0` (or `21.2.0` for a few)
  in its own JSDoc in the type file — this is genuinely a preview API in
  this Angular version, not a stable one being treated cautiously.

## pristine / dirty / touched / submitted, concretely

- **dirty** — `FieldState.dirty: Signal<boolean>`, true once the value has
  changed. This is Signal Forms' equivalent of reactive forms' `dirty`.
- **touched** — `FieldState.touched: Signal<boolean>`, true after the field
  has received then lost focus (the native `blur` event specifically — see
  the "genuine mistake" about this below).
- **pristine** — **no direct equivalent.** Signal Forms exposes `dirty`, not
  `pristine`; `!dirty()` is the closest expression of "untouched by edits,"
  and that is what the `PRISTINE` test asserts. This is recorded here
  because interpretation 6 requires saying so plainly when a concept has no
  direct equivalent, not glossing over the naming difference.
- **submitted** — no single boolean field either. It is expressed two ways
  in this build: through `onInvalid` firing (a failed attempt) and through
  `submitting()`'s true→false transition (an attempt that was made,
  in-flight, then settled). There is no `FieldState.submitted` signal to
  read directly.

## Real limitations and mistakes hit while building

All four are logged in full, with real error text, in
`verification-log.md` §4: a real TypeScript compile error (`quoteForm` was
wrongly `protected`), a wrong assumption about which native event marks a
field touched (`blur`, not `focusout` — found by reading the compiled
`fesm2022/signals.mjs`, not the type definitions, which don't say), a
zoneless async-timing gap specific to bridging `HttpClient`'s Observable
into `submit()`'s required Promise return type, and an assumption
(`markAsTouched()` calls copied from the reactive form's pattern) that
testing proved genuinely unnecessary. None of these were manufactured to
have something to report — they are what actually happened, in the order
they happened.

## Resolved interpretations, in full

1. **Which API and contract.** `day-3/task-3/QuotesApi`, read fresh rather
   than trusted from Task 1's notes. `POST /api/quotes` matches the
   Academy's shorthand exactly. `CreateQuoteRequest(string Text, string?
   Author = null)` carries zero validation attributes on either field; both
   `required()` calls here are the same kind of client-only, directed UX
   decision already documented on the reactive version, not mirrored server
   constraints.
2. **Replication, not rebuild or migration.** Carried from `day-14/task-1`
   at commit `32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba`. The existing
   `QuoteApi` service and `Quote`/`CreateQuoteRequest` model types are
   reused by the new form exactly as they are by the reactive one — no
   parallel service or model was written.
3. **Live calls.** None. Every test — Signal Forms, reactive, and parity —
   uses `HttpTestingController`. No token was obtained, generated, or
   hardcoded.
4. **Experimental API, honest limits.** See "Real limitations" above and
   `comparison.md`. No workaround was hand-rolled and presented as Signal
   Forms doing the work; where a real gap was hit (bridging to a Promise),
   it is named as exactly that.
5. **The comparison.** `comparison.md`, every claim anchored to something
   hit in this specific form (see that file for the anchors).
6. **The four states.** `pristine`/`dirty`/`touched`/`submitted` — see
   above; `pristine` has no direct equivalent and is expressed via `!dirty()`.
7. **Evidence.** Every version, path, error, and count in this file and in
   `submission.md` comes from a captured file under `output/` or a command
   actually run in this session.

## Angular version — unchanged from the carried app

`ng version` reports `Angular CLI: 21.2.21`, unchanged from `day-14/task-1`
and both Day 13 apps. No dependency was upgraded; `npm install` restored
the exact carried lockfile.

## How to run and test

```bash
cd day-14/task-2
npm install                     # already done; committed package-lock.json pins the tree
npx ng serve --proxy-config proxy.conf.json   # http://localhost:4200/
npx ng build                    # production build
npx ng test --watch=false       # Vitest suite, no network/Docker/credentials required
node scripts/verify-structural.mjs
```

Both forms are on the same page once signed in (see `day-14/task-1`'s
carried dev-login for the local sign-in flow) — "Create a quote — reactive
forms" first, "Create a quote — Signal Forms (preview)" directly below it.
