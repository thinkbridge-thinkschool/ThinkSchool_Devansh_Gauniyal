# Day 4 — Task 5: Add OpenTelemetry tracing

## What changed and where

All changes to the real app are in `day-3/task-3/QuotesApi` (approved before making them):

- `QuotesApi.csproj` — added `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` (all pinned to stable `1.17.0`). **No** `OpenTelemetry.Instrumentation.EntityFrameworkCore` — this app doesn't use EF Core at all (confirmed by grep before touching anything), and that package has no stable release regardless (latest is `1.17.0-beta.1`), so there was no reason to add it.
- `Telemetry.cs` — new file, a static `ActivitySource` for the app's one custom span.
- `Program.cs` — registers OpenTelemetry tracing with a resource service name, the automatic instrumentations, and the OTLP exporter (skipped in the `"Testing"` environment); fixes the correlation middleware to use the real OTel trace ID instead of ASP.NET Core's unrelated request identifier (see below).
- `Tokens/RefreshTokenService.cs` — one custom span around `Rotate(...)`.

New test project `day-4/task-5/QuotesApi.Tracing.Tests` (`ProjectReference` only, no Day 3 source duplicated).

## The OTel configuration, explained

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(Telemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!builder.Environment.IsEnvironment("Testing"))
        {
            tracing.AddOtlpExporter();
        }
    });
```

`AddAspNetCoreInstrumentation()` gives every incoming HTTP request its own span, automatically, with `http.route`/`http.request.method`/`http.response.status_code` tags — no code change needed anywhere in the endpoint handlers. `AddHttpClientInstrumentation()` would do the same for any outbound `HttpClient` call this app made, but it currently makes none (also confirmed by grep) — it's registered anyway since it's a stable, harmless no-op today and the task's own config line includes it; if this app ever calls another service over HTTP, that call gets a span for free the moment it happens.

`.ConfigureResource(resource => resource.AddService(Telemetry.ServiceName))` sets the exported resource's `service.name` attribute to `"QuotesApi"`. Skipping this is the single most common way this exercise looks broken in a screenshot — without it, every trace backend (Jaeger included) shows the service as `unknown_service`, and it's not obvious from the trace data itself why.

`AddOtlpExporter()` is genuinely skipped in the `"Testing"` environment — every `WebApplicationFactory` in this repo (Tasks 2, 4, and 5's own test projects) calls `builder.UseEnvironment("Testing")`, and none of them have a collector to talk to. Without this guard, every test run would try to open a real network connection to `localhost:4317` and either hang waiting for a connection, or spam warnings, on every single test. Where it isn't skipped, `AddOtlpExporter()` reads the standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable itself (no code needed for that either), defaulting to `http://localhost:4317` when unset — so the endpoint is configurable without ever hardcoding it.

## Automatic instrumentation vs. the custom span

Automatic instrumentation covers "a request came in" and "we called out over HTTP." It does **not** cover in-process logic with no I/O boundary of its own. `RefreshTokenService.Rotate(...)` is exactly that: a hash lookup, an expiry check, a revoked/reuse check that can revoke an entire token family, then either rejection or rotation — real branching logic that would otherwise be invisible time sitting inside the parent request span, with no indication of *why* it took as long as it did or *which* of several outcomes occurred. The custom span:

```csharp
using var activity = Telemetry.Source.StartActivity("refresh-token.rotate");
...
activity?.SetTag("user.id", stored.UserId);
activity?.SetTag("refresh_token.outcome", "rotated"); // or "reuse_detected" / "already_revoked" / "not_found_or_expired"
```

`activity` is nullable by design — `StartActivity` returns `null` when nothing is listening (e.g., no `TracerProvider` built at all), so every tag-set is null-conditional (`activity?.SetTag(...)`) rather than assumed non-null.

## The custom span's tags

- `refresh_token.outcome` — one of `rotated`, `reuse_detected`, `already_revoked`, `not_found_or_expired`. Not sensitive; a plain outcome label.
- `user.id` — the same `UserId` already logged elsewhere in this codebase (Tasks 2/4). It's PII: an identifier tied to a real person, and in a real deployment it would be subject to whatever log/trace retention policy applies to identifiers, not kept indefinitely just because it's convenient. An email address is never tagged, for the same reason Task 4 never logs one.

## Resource/service name

Covered above — `Telemetry.ServiceName = "QuotesApi"`, set via `ConfigureResource`, is what makes the trace backend show a real service name instead of `unknown_service`.

## Logs and traces: what the investigation actually found

The task page claims "the OTel TraceId is the same one Serilog emits — your logs and traces correlate automatically." **That was false for this app before this task.** Task 4's correlation middleware pushed `ctx.TraceIdentifier` — ASP.NET Core's own per-request identifier (a string like `0HN7...:00000001`), completely unrelated to OpenTelemetry's W3C trace ID (a 128-bit value rendered as 32 hex characters). They would never have matched.

The fix, approved before touching Task 4's file:

```csharp
var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
using (LogContext.PushProperty("TraceId", traceId))
```

This prefers the real OTel trace ID (available once `AddAspNetCoreInstrumentation()` creates an `Activity` for the request, which happens in every environment including `"Testing"`), falling back to `ctx.TraceIdentifier` only if no `Activity` is active for some reason. **Verified for real**, not just implemented: a captured log line and the exported span for the same refresh request both carry `15a39d0df2266033e1c4e61ae341751e` — see `sample` evidence below and the automated test `LoginRequest_LogLineAndTraceShareTheSameTraceId`, which asserts this equality on every test run.

```
Serilog: [14:49:07 INF] (15a39d0df2266033e1c4e61ae341751e) Refresh token rotated for user otel-demo-user in family 45469c7a-a75d-4df2-b44f-978091619df7
Jaeger:  traceID: 15a39d0df2266033e1c4e61ae341751e
           span: 'POST /api/auth/refresh'   spanID=8fd1ec81c01acc48  (root)
           span: 'refresh-token.rotate'     spanID=52a73d2839f328d2  parent=8fd1ec81c01acc48
             tag: refresh_token.outcome=rotated
             tag: user.id=otel-demo-user
```

## What is deliberately not tagged, and why

Same rule as Task 4's logging, applied to span tags: never a password, access token, refresh token, complete JWT, `Authorization` header, client secret, or connection string. `user.id` is tagged (not an email) for the same PII-minimization reason given above.

## Reproducing this locally

```
docker run -d --name jaeger \
  -p 16686:16686 -p 4317:4317 -p 4318:4318 \
  -e COLLECTOR_OTLP_ENABLED=true \
  jaegertracing/all-in-one:1.76.0
```

Dashboard: http://localhost:16686 — search service `QuotesApi`. Both container ports are local-only; nothing is exposed beyond the host. Stop and remove afterwards with `docker stop jaeger && docker rm jaeger`.

## Tests

`day-4/task-5/QuotesApi.Tracing.Tests` — new project, `ProjectReference` to `QuotesApi.csproj` only:

- `RefreshTokenRotate_CreatesChildSpan_WithExpectedTagsAndParent` — attaches a raw `System.Diagnostics.ActivityListener` (no Docker, no OTLP, no exporter needed — this is a first-class .NET diagnostics API), sends a real login + refresh through a `WebApplicationFactory`-hosted instance, and asserts the `refresh-token.rotate` activity exists, has a non-null parent (proving it's genuinely nested under the request span), and carries the expected tags.
- `LoginRequest_LogLineAndTraceShareTheSameTraceId` — the automated version of the correlation verification above: captures both a Serilog log event (via the same in-memory-sink DI seam Task 4 added) and an `Activity` for the same request, and asserts their trace ID strings are identical. Would fail immediately if the Step 4(b) fix were reverted.

All 60 tests across four test projects pass (19 original + 37 from Task 2 + 2 from Task 4 + 2 new here) — see the completion report for genuine command output. Task 1's CI gate is unaffected since it only builds a separate, unrelated project.
