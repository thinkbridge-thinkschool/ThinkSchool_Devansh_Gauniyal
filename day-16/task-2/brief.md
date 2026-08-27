Take the Angular 21 app from day-16/task-1 and copy it into day-16/task-2 untouched. Do not modify day-16/task-1 or anything earlier.

Working inside day-16/task-2 only, consolidate the quotes feature's state into a single signal-based store service. The API is the QuotesApi at day-3/task-3/QuotesApi — read its controller and DTOs first and use the ACTUAL routes and the ACTUAL field names with exact casing. Do not invent a field.

The store must:
- be a service holding the feature's state in signals: the quotes collection, the selected quote, a loading flag, and an error value
- expose read-only signals to components — components must not be able to write to the state directly, only call the store's methods
- derive anything derivable with computed rather than storing it twice, so there is one source of truth
- own the API calls for this feature, so components ask the store rather than calling HttpClient themselves
- handle concurrent updates correctly: if two loads or two selections overlap, a stale response must not overwrite newer state. The app already has a stale-response guard for the detail; the store must preserve that behaviour rather than reintroducing the bug.
- keep the existing loading, error and empty states working and observable

Then refactor the existing components to read from the store instead of holding their own copies of this state — but change only what that requires. Do not restructure components beyond moving state access to the store.

Separately, draft a rule for when this feature should move from signals to a signal store or NgRx. Ground it in this app's real numbers and names — how many signals this store holds now, which components read it, what specifically would have to grow for the rule to trigger. Not a generic checklist.

Everything already in the app — the list, the detail, both forms, the interceptors, the routing, the guard, the lazy load, the characterization test — must keep working. Do not degrade any existing test. Change nothing else: no refactors beyond the state move, no renames, no dependency upgrades, no restyling.
