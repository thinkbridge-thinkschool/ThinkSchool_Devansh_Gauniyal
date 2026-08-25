Continue the Angular 21 app I built on Day 13 — the quotes list-plus-detail component. Copy that app into the Day 14 folder untouched, then ADD a reactive create-a-quote form to it as a new feature. The existing list and detail must keep working, and Day 13's folder must not be modified.

The form goes against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Read the POST endpoint's route, its request DTO and every validation attribute on that DTO. The form shape and every validator must come from those actual fields and constraints. Do not invent a field, do not omit a required one, and do not pick a length limit that differs from the API's. Reuse the model types and service the Day 13 app already established rather than writing a parallel copy.

Requirements:
- a reactive form using Angular's ReactiveFormsModule, standalone component, no NgModule anywhere
- one validator per real API constraint, matching its limits exactly — required, maximum length, and anything else the DTO enforces
- error messages that appear only after the field is touched or the form is submitted, never on an untouched empty form
- full accessibility wiring:
    * every input has a <label for="..."> bound to that input's id
    * aria-invalid reflects the field's real validity state
    * aria-describedby points at the id of the element containing that field's error message, and that element must exist in the DOM whenever the attribute references it
    * the whole form is operable by keyboard alone, with a visible focus indicator
    * on submit with errors, focus moves programmatically to the first invalid field
- four states handled distinctly: empty, invalid, submitting (control disabled or a busy indicator), and server-error (the API rejected it)
- a server error must surface to the user and be announced to assistive technology, never swallowed
- inject() for every dependency, never constructor injection; the model fully typed, no `any`
- a successful submit should be reflected in the existing list where that is natural, without breaking the Day 13 stale-response guard

Do not add form libraries, UI component libraries, validation libraries, or anything beyond what was already in the Day 13 app. Zoneless is the Angular 21 default — do not re-add Zone.js.
