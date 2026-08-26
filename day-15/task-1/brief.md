Take the Angular 21 app from day-14/task-2 and copy it into day-15/task-1 untouched. Do not modify day-14 or anything earlier.

Working inside day-15/task-1 only, do these in this order.

FIRST, before touching any UI or any interceptor: write a characterization test that pins my real Week-1 API contract. The API is the QuotesApi at day-3/task-3/QuotesApi. Read its controller and DTOs and pin what is actually there — the real list route, the real pagination parameters if it has any, the real response field names with exact casing, and the real error shape the API returns on a 4xx. If the endpoint is reachable without a token, call it for real and pin the observed response; if not, pin from the DTO source and say so. Run that test and show it green before writing anything else.

THEN wire HttpClient with functional interceptors against that pinned contract:
- an auth header interceptor that attaches the bearer token to requests going to my API and to nothing else; the token comes from a service or config, never a hardcoded string, and is never committed
- a retry interceptor that retries ONLY idempotent GET requests, ONLY on transient failures — network errors and 5xx — never on 4xx, never on POST, PUT or PATCH, with exponential backoff and a hard cap on attempts
- an error-mapping interceptor that turns a ProblemDetails or ValidationProblemDetails response into a typed application error carrying a friendly message, and for ValidationProblemDetails preserves the per-field errors so a form could show them

State and justify the interceptor order, and make sure the error mapping sees the final outcome rather than an intermediate retry.

Everything already in the app — the list, the detail, the stale-response guard, both forms — must keep working. Do not degrade any existing test. Change nothing else: no refactors, no renames, no dependency upgrades, no restyling.
