Take the Angular 21 app from day-15/task-1 and copy it into day-16/task-1 untouched. Do not modify day-15 or anything earlier.

Working inside day-16/task-1 only, add routing against my real Week-1 API — the QuotesApi at day-3/task-3/QuotesApi. Read its controller and DTOs first and use the ACTUAL list route, the ACTUAL detail route, and the ACTUAL identifier field name and type. Do not invent an endpoint, a field name or a param type.

Build:
- a route table with the quotes list as a route, and a quote detail as a LAZY-LOADED route using loadComponent — the detail component must not appear in the main bundle
- a route param carrying the real quote id, typed to match the API's real id type, read with the modern input-binding or ActivatedRoute approach the app already uses
- a functional auth guard, a CanActivateFn, protecting the detail route: it returns true when authenticated and returns a UrlTree redirect when not. Return a UrlTree rather than calling router.navigate, so the navigation is cancelled cleanly instead of racing a second one.
- handling for a missing or invalid route param — a param that is absent, malformed for the id's real type, or refers to a quote that does not exist. Each must produce a sensible outcome, not a crash or a blank screen.
- a View Transition between the list and the detail using the router's withViewTransitions, with matching view-transition-name styling on the elements that should morph. If the browser does not support View Transitions, navigation must still work normally — verify the fallback rather than assuming it.

Everything already in the app — the list, the detail, the stale-response guard, both forms, the interceptors, the characterization test — must keep working. Do not degrade any existing test. Change nothing else: no refactors, no renames, no dependency upgrades, no restyling beyond the view-transition-name styles the transition requires.
