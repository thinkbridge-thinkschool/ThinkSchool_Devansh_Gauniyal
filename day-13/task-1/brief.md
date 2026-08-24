Build a standalone Angular 21 component against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi in this repository. Use its actual endpoint path and its actual response field names; read the controller and DTOs to get them exactly right, and do not invent or guess a field.

The component must:
- be standalone, with no NgModule anywhere in the project
- hold at least two pieces of state in signal()
- derive at least one value with computed() from BOTH of those signals, so changing either one changes the result
- render the list of quotes with @for, using a track expression keyed on the real identifier field
- use @if to handle the empty and loading states, and @switch for at least one multi-branch case
- obtain every dependency with inject(), never constructor injection
- fetch via a small service that returns the real API shape

Do not add state-management libraries, UI component libraries, or anything beyond what the Angular CLI scaffolds. Zoneless is the Angular 21 default — do not re-add Zone.js and do not call provideZonelessChangeDetection(), which is no longer required.
