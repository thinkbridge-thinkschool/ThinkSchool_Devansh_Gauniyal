# Provenance — Day 14 Task 2

This app is a copy-forward of `day-14/task-1`, plus a Signal Forms version of
the create-a-quote form added alongside the existing reactive-forms one.
Verified with `diff -rq day-14/task-1 day-14/task-2` (excluding
`node_modules`, `dist`, `.angular`, `output`, and this task's own new
top-level docs) — the list below is exactly what that diff reported.

- **Source path copied from:** `day-14/task-1`.
- **Source commit hash:** `32f9dabcbce3cdb70d9d05a2065b3c64ee8fd3ba`
  ("Record Devansh's own VoiceOver observation, no longer PENDING" — the tip
  of `day-14/task-1` at copy time, confirmed with
  `git log -1 --format=%H -- day-14/task-1`).
- **Copy command used:**
  ```bash
  rsync -a --exclude='node_modules' --exclude='dist' --exclude='.angular' --exclude='coverage' --exclude='output' --exclude='.git' \
    day-14/task-1/ day-14/task-2/
  ```
  followed by `npm install` in `day-14/task-2` to restore dependencies from
  the copied `package-lock.json` (no `node_modules` was copied across).

## Carried unchanged (byte-identical to day-14/task-1)

- `.editorconfig`, `.gitignore`, `.prettierrc`, `.vscode/*`
- `angular.json`, `package.json`, `package-lock.json`
- `tsconfig.json`, `tsconfig.app.json`, `tsconfig.spec.json`
- `public/favicon.ico`
- `proxy.conf.json`
- `src/index.html`, `src/main.ts`, `src/styles.css`
- `src/app/app.css`, `src/app/app.config.ts`, `src/app/app.spec.ts`
- `src/app/auth/dev-login/*`, `src/app/auth/dev-token.interceptor.ts`
- `src/app/quotes/quote.ts`, `src/app/quotes/create-quote-request.ts`,
  `src/app/quotes/quote-api.ts`, `src/app/quotes/quote-api.spec.ts`
- `src/app/quotes/create-quote-form/*` (the entire reactive form — component,
  template, styles, and its own spec file — kept exactly as-is per the task's
  explicit instruction to keep both forms, not migrate or replace either)
- `src/app/quotes/quote-browser/*` (list, detail, and the Day 13
  stale-response guard)

## Modified (carried, then changed — each justified)

- `src/app/app.ts` — added an import for `CreateQuoteFormSignals` and listed
  it in the component's `imports` array, so the new form can be hosted.
  Nothing else in this file changed.
- `src/app/app.html` — added a second `<section class="card card--primary">`
  hosting `<app-create-quote-form-signals>` next to the existing reactive
  one, and retitled both section headings ("— reactive forms" /
  "— Signal Forms (preview)") so a mentor can tell them apart at a glance.
  Both forms feed the same `justCreated` signal so either one's successful
  submit reflects in the quotes list below. No other markup changed.
- `scripts/verify-structural.mjs` — the eight carried checks are untouched;
  two new checks appended: the same aria-describedby-resolvability check
  applied to `create-quote-form-signals.html`, and a check that the new
  component imports from the real `@angular/forms/signals` path (Phase 5
  explicitly requires this).

## Newly added (Day 14 Task 2 only)

- `src/app/quotes/create-quote-form-signals/create-quote-form-signals.ts`
- `src/app/quotes/create-quote-form-signals/create-quote-form-signals.html`
- `src/app/quotes/create-quote-form-signals/create-quote-form-signals.css`
  (a copy of `create-quote-form/create-quote-form.css`, so the two forms are
  visually consistent — no new styling was invented)
- `src/app/quotes/create-quote-form-signals/create-quote-form-signals.spec.ts`
- `src/app/quotes/create-quote-form-parity.spec.ts` — tests that instantiate
  both forms together and compare their request bodies directly.
- `brief.md`, `README.md`, `verification-log.md`, `comparison.md`,
  `submission.md`, `PROVENANCE.md` (this file) — day-14/task-1's own versions
  of these documented Task 1's exercise, not Task 2's, so they were removed
  from the copy immediately after the `rsync` and replaced with fresh ones.
- `output/*` — fresh captured evidence for this task; day-14/task-1's own
  `output/*` was excluded from the copy entirely (it documents Task 1's own
  test/mutation runs).

## Not carried

`day-14/task-1/brief.md`, `README.md`, `verification-log.md`, `submission.md`,
`PROVENANCE.md`, and `output/*` were excluded from the `rsync` (the first
three via explicit `--exclude` flags for `output`, and the docs were removed
immediately after copying since `rsync` has no clean way to exclude files
that don't yet have a name pattern distinguishing them from reusable
content). `day-14/task-1` itself was never touched — only the copy inside
`day-14/task-2` was edited, and only in the three files listed above under
"Modified."
