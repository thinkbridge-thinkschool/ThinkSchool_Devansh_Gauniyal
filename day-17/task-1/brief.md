Take the Angular 21 app from day-16/task-2 and copy it into day-17/task-1 untouched, then deploy it to Azure Static Web Apps on the free tier at [SWA URL]. Do not modify day-16 or anything earlier.

The Week-1 API is the QuotesApi at day-3/task-3/QuotesApi. Copy it into day-17/task-1 as well and deploy that copy to Azure App Service on the free F1 tier at [API URL]. Do not modify the original under day-3.

Its real endpoints: GET /api/quotes returns a list of quotes shaped { id: number, ownerId: string, text: string, author: string | null } and is anonymous. GET /api/protected and POST /api/quotes both require authorization. POST takes { text: string, author?: string }. The identifier field is id, an int. There are no validation attributes on any request DTO.

The auth requirement is Managed Identity with no client secret anywhere. The API already has an Entra JWT scheme; point it at tenant YOUR_TENANT_ID with audience api://YOUR_CLIENT_ID. Create an Azure Function App with a system-assigned managed identity that acquires a token for that audience and calls one of the AUTHORIZED endpoints — not the anonymous list. The Angular app calls the Function, the Function calls the API.

No secret may exist in the repository, in the Static Web App's settings, in the Function App's settings, or in the API's settings. No client secret, no connection string, no key, no stored token. If you cannot make something work without one, stop and say so rather than falling back to a secret.

The Angular app currently hardcodes the relative path /api/quotes. Make the base URL configurable so it can point at the deployed Function, without breaking any of the 107 existing tests.

Run Lighthouse against the live URL with Chrome and report the real scores, whatever they are.
