# Signal Forms vs. reactive forms — this form, specifically

Both versions build the same create-a-quote form against the same real
`POST /api/quotes` (`day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs`'s
`CreateQuoteRequest(string Text, string? Author = null)`, neither field
carrying a validation attribute). Every claim below is anchored to something
actually hit while building `create-quote-form-signals.ts`/`.html`, checked
against `create-quote-form.ts`/`.html` (the reactive version, unchanged).

## Where Signal Forms was simpler here

- **`submitting` is free.** The reactive version needs its own
  `signal(false)` plus three manual `.set(true)`/`.set(false)` calls spread
  across `onSubmit()`'s guard clause, its success branch, and its error
  branch. The Signal Forms version reads `quoteForm().submitting()` directly
  off the field state — the framework sets it while the `action` promise is
  in flight and clears it when the promise settles, success or failure. Zero
  manual bookkeeping, and no risk of forgetting to reset it on one of the
  branches (a real bug shape in hand-written reactive code).
- **No `submitAttempted` flag was needed.** The reactive version tracks a
  private `submitAttempted` boolean specifically so `showError()` can show
  errors after a failed submit even on an untouched field. In the Signal
  Forms version, `submit()`'s `onInvalid` callback already leaves the
  attempted fields `touched()` by the time it runs — this was a genuine
  wrong assumption caught while building (see `verification-log.md`): an
  early draft added manual `markAsTouched()` calls to be safe, mirroring the
  reactive form's `markAllAsTouched()`, and deleting them again to test the
  assumption showed every touched/error-display test still passed. One less
  piece of state to carry per form.
- **Declarative submission wiring.** `form(model, schema, { submission: { action, onInvalid } })`
  plus `<form [formRoot]="quoteForm">` replaces the reactive version's
  `(ngSubmit)="onSubmit()"` + a hand-written method that checks validity,
  marks things touched, and manages state around the HTTP call. The
  structure is declared once at construction instead of imperatively
  assembled in a method body.

## Where it is still rough here

- **Bridging to `HttpClient` needs a hand-rolled Promise.** `submit()`'s
  `action` must return a `Promise`, but `QuoteApi.createQuote()` (shared with
  the reactive form, unchanged) returns an RxJS `Observable`. The reactive
  version's `.subscribe({ next, error })` is idiomatic; the Signal Forms
  version needs `new Promise<Quote>((resolve, reject) => { ...subscribe...
  })` to bridge the two — boilerplate the reactive version simply doesn't
  have, since `ReactiveFormsModule` was never designed against RxJS-shaped
  async at the framework's submission layer the way it is here.
- **A real timing gap surfaced in testing, not just in the app.** The
  `FAILED SUBMIT` test genuinely failed on the first attempt because
  `fixture.whenStable()` — normally sufficient in this app's other,
  reactive-forms tests — resolved before the hand-rolled Promise's
  microtasks had actually run in this zoneless app; only an explicit
  macrotask tick reliably drained them (see `verification-log.md`). This is
  specifically a consequence of the Promise bridge above, not something the
  reactive form's Observable-native testing pattern ever has to deal with.
- **Which native DOM event marks a field touched is undocumented in the
  type signatures and had to be found in the compiled source.** `blur`
  (verified in `node_modules/@angular/forms/fesm2022/signals.mjs`:
  `host.listenToDom('blur', () => parent.state().markAsTouched())`), not
  `focusout`. `.d.ts` files and the public docs comments say nothing about
  which event; this had to be discovered empirically. Reactive forms'
  `updateOn: 'blur'` behavior, by contrast, is documented directly on
  `AbstractControlOptions`.
- **No parity claim beyond what was tested.** Only `required()` was
  exercised here, since that's the only real constraint on this DTO. Whether
  `min`/`max`/`pattern`/async validation are as ergonomic as their reactive
  equivalents was not observed in this form and is not claimed.
