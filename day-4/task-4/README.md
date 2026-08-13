# Day 4 — Task 4: Serilog with correlation IDs

## What changed and where

All changes to the real app are in `day-3/task-3/QuotesApi` (approved before making them):

- `QuotesApi.csproj` — added `Serilog.AspNetCore`, `Serilog.Settings.Configuration`, `Serilog.Sinks.Console`.
- `Program.cs` — replaced the default logger with Serilog (`builder.Host.UseSerilog(...)`), added the correlation middleware, added structured log statements to the login handler.
- `Tokens/InternalAccessTokenService.cs` and `Tokens/RefreshTokenService.cs` — constructor-injected `ILogger<T>` and added structured log statements to token issuance and refresh-rejection paths.
- `appsettings.json` — removed the old `"Logging"` section (Serilog doesn't read it) and replaced it with a `"Serilog"` section.
- `appsettings.Development.json` — new, tracked, containing only the EF Core dev-only override (see below).

New test project `day-4/task-4/QuotesApi.Logging.Tests` (`ProjectReference` only, no Day 3 source duplicated) — an in-memory Serilog sink and two tests asserting the correlation behavior actually holds.

## The Serilog setup, explained

`builder.Host.UseSerilog((context, services, configuration) => ...)` is the modern host-integrated pattern (as opposed to the older `Log.Logger = new LoggerConfiguration()...CreateLogger()` static-assignment pattern). This matters specifically because two existing test suites (Task 2's, 56 tests total) each spin up their own `WebApplicationFactory<Program>` instance — a static `Log.Logger` would be global process-wide state that every new test-hosted instance would stomp on. The host-integrated pattern scopes Serilog's lifecycle to each individual host, so 58 tests across three projects (see "Tests" below) can each build their own app instance without interfering with each other's logging.

`.ReadFrom.Configuration(context.Configuration)` reads the `"Serilog"` section (log levels — see below). `.Enrich.FromLogContext()` makes any property pushed via `LogContext.PushProperty(...)` — specifically our `TraceId` — attach to every log event emitted while that context is active. `.WriteTo.Console(outputTemplate: "...")` is the actual sink, with an output template that explicitly renders `{TraceId}`. This last part matters more than it looks: the correlation ID is genuinely attached to every log event regardless of the output template, but if the template doesn't render the property, the correlation is invisible in the terminal even though it's there — this is the most common way this exact exercise silently "works" (code compiles, nothing errors) while producing no visible evidence that correlation exists at all.

There's also a small, deliberate test-only seam: the `UseSerilog` delegate also adds any `Serilog.Core.ILogEventSink` registered in DI (a `foreach` over `services.GetServices<ILogEventSink>()`). In production, nothing registers one, so this is a no-op. In `QuotesApi.Logging.Tests`, `LoggingApiFactory` registers an `InMemorySink` this way, letting a test assert on real emitted `LogEvent`s (which properties they carry, whether they share a `TraceId`) without parsing console text or touching process-wide `Console.Out` (which would be genuinely unsafe under xUnit's parallel test execution — different test classes run concurrently by default, and redirecting the real console output would let one test's captured text get polluted by another test's concurrently-running app instance).

## Why message templates beat string interpolation

`logger.LogInformation("Login succeeded for user {UserId}", caller.UserId)` creates a log event with an **indexed property** named `UserId` and a queryable value — a log viewer (or, later, App Insights/KQL) can filter or aggregate on `UserId` directly. `logger.LogInformation($"Login succeeded for user {caller.UserId}")` instead bakes the value into a flat string at the call site; by the time it reaches any sink, there's no structured field left, only text that has to be parsed (with a regex, or not at all) to extract anything. The whole point of structured logging is that the fields survive past the point where the string was originally formatted.

## Correlation middleware: how it works and why position matters

```csharp
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();
```

`LogContext.PushProperty` pushes a property onto an ambient (async-local) context that every log call made anywhere downstream — inside this request's call stack — will pick up automatically via `Enrich.FromLogContext()`, without needing to pass the value around explicitly. The `using` block scopes it to exactly this request: it's pushed before `next()` runs the rest of the pipeline, and popped once that request finishes, so it can never leak into a different request's logs.

Position matters because the property is scoped to *this middleware's remaining pipeline*, not the whole app. It's registered before `UseAuthentication()`/`UseAuthorization()` and before any endpoint, so authentication failures, authorization failures, and every log statement inside an endpoint handler all happen "inside" the `using` block and inherit the property. `app.UseSerilogRequestLogging()` — which logs one summary line per request ("HTTP POST ... responded 200 in Xms") — is placed right after the correlation middleware for the same reason: if it were registered *before* the correlation middleware instead, its own summary log line would be emitted outside the `using` block and would be missing the `TraceId`, which is exactly the kind of subtle ordering bug that makes correlation silently incomplete rather than obviously broken.

## Log levels: why `Serilog:MinimumLevel` and not `Logging:LogLevel`

Once `UseSerilog` replaces the framework's default logging provider, the standard ASP.NET Core `"Logging": { "LogLevel": {...} }` configuration section is no longer read by anything — it was specific to the default `ILoggerFactory` provider, which Serilog has replaced. Leaving that section in `appsettings.json` would silently do nothing while looking like it's doing something, so it was removed rather than left as misleading dead configuration. Serilog reads its own `"Serilog"` section instead, via `Serilog.Settings.Configuration`'s `ReadFrom.Configuration(...)`:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

`Default: Information` covers this app's own code. `Override: {"Microsoft.AspNetCore": "Warning"}` quiets the framework's own routing/hosting `Information`-level noise, which is chatty enough to bury the app's own log lines if left at its default.

The EF Core override (`"Microsoft.EntityFrameworkCore.Database.Command": "Debug"`) is deliberately **not** in the base `appsettings.json` — it belongs in `appsettings.Development.json`, so verbose SQL command logging only happens locally, never in a deployed environment where it would add noise and cost. **This app currently doesn't use EF Core at all** (confirmed by grepping for `DbContext`/`UseSqlite`/`UseSqlServer` across the whole project — no matches, and the csproj has no EF package), so this specific override is a no-op today. It's included anyway because it's the textbook-correct location for it and costs nothing; if EF Core is added later (as it was in the separate `Quotes.Api` lineage from `day-3/task-5` onward), the override starts working immediately without further changes.

ASP.NET Core's configuration system merges `appsettings.json` and `appsettings.{Environment}.json` into one `IConfiguration` tree before Serilog ever sees it (later files override matching keys, but distinct keys like two different `Override` entries both survive), so the dev file's override is additive to the base file's, not a replacement of it.

## `Activity.Current.TraceId` vs. `ctx.TraceIdentifier`

The task specifies `ctx.TraceIdentifier` (`HttpContext.TraceIdentifier`), which is what's implemented here. Worth noting for the record: in a distributed system spanning multiple services, `Activity.Current?.TraceId` (the W3C Trace Context standard, the same ID that OpenTelemetry propagates across a `traceparent` HTTP header) is generally the more standard choice, since it's designed to survive a hop between services and remains consistent with the trace IDs a distributed tracing backend would show. `HttpContext.TraceIdentifier` is scoped to a single process's handling of one request and doesn't propagate anywhere by itself — it's the right choice for this exercise's stated scope (correlating log lines within one request in one service) but would need to be `Activity.Current?.TraceId.ToString()` instead if this app's logs needed to line up with traces from a downstream service.

## What is deliberately not logged, and why

Never logged, anywhere: the login password (raw or hashed), the access token, the refresh token, the complete JWT, the `Authorization` header, or the request body of the login/refresh endpoints. The login-failure branch specifically avoids logging the submitted email too — it's unvalidated input, and echoing it into logs would let it be used to enumerate which addresses "look real" against the single configured internal account, while adding nothing needed to explain what happened (there's only one configured caller either way).

Where an identifier does appear (`UserId`, e.g. `"demo-user-1"` in the captured sample), it's a user ID rather than an email specifically because it's the more privacy-preserving choice per the task's own instruction — and it's still PII, still an identifier tied to a person, and would be subject to a real log retention policy in production rather than kept indefinitely.

## Tests

`day-4/task-4/QuotesApi.Logging.Tests` — new project, `ProjectReference` to `QuotesApi.csproj` only, no Day 3 source duplicated:

- `SingleRequest_LogsShareOneTraceId_AndDifferentRequestsGetDifferentIds` — sends two separate login requests through one `WebApplicationFactory`-hosted instance, captures every emitted `LogEvent` via an in-memory sink, and asserts: at least two distinct `TraceId` values exist (proving it's genuinely per-request, not a hard-coded constant), and each `TraceId` group contains at least two log lines (proving correlation actually links multiple log statements together, not just one).
- `SingleLoginRequest_ProducesAtLeastFiveLogLines` — asserts a single login request produces at least 5 log events all sharing one `TraceId`, which would fail if a future change removed one of the log statements added in this task.

Both tests would fail for a real reason if the middleware were removed, moved to the wrong pipeline position, or if `Enrich.FromLogContext()` were dropped — they don't just assert `NotNull` or that a method was called.

All 58 tests across the three test projects (`QuotesApi.Tests`'s original 19, Task 2's 37, and these 2 new ones) pass — see the completion report for genuine command output.
