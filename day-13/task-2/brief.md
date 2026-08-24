Build a quotes list-plus-detail component in Angular 21 against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Read its controller and DTOs and use the actual route paths and the actual response field names, exactly as spelled and cased. Do not invent, guess or "tidy" a field name. Name the endpoints and fields you used in a comment at the top of the service.

Requirements:
- standalone components, no NgModule anywhere
- three signals covering loading, error and data, for BOTH the list and the selected detail
- inject() for every dependency, never constructor injection
- the model fully typed against the real API — no `any` anywhere, no `as any`, no implicit any; TypeScript strict mode on
- selecting an item in the list loads its detail; the two requests can interleave, so a response for a selection that is no longer current must be discarded rather than applied
- an error from either request must surface in the error signal — never swallowed, never converted into an empty result
- render with the new control flow, @for with a real track expression keyed on the actual identifier field, and @if for the loading, error and empty states

Do not add state-management libraries, UI component libraries, HTTP wrappers, or anything beyond what the Angular CLI scaffolds. Zoneless is the Angular 21 default — do not re-add Zone.js and do not call provideZonelessChangeDetection().
