# Day 4 — Task 5: Add OpenTelemetry tracing

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-5/day-4/task-5

## Notes for your mentor

Wired OpenTelemetry tracing into the real Day 3 auth API (`day-3/task-3/QuotesApi`) — approved before touching it, modifying `Program.cs`, `QuotesApi.csproj`, and `Tokens/RefreshTokenService.cs`. Along the way I checked the task's claim that "logs and traces correlate automatically" and found it was actually false in this codebase: Task 4's correlation middleware used `ctx.TraceIdentifier`, not OpenTelemetry's trace ID. Fixed (approved separately) to use `Activity.Current?.TraceId`, and verified for real — a captured log line and the exported span for the same request carry the identical trace ID (`15a39d0df2266033e1c4e61ae341751e`). Screenshot at `day-4/task-5/trace-screenshot.png` shows the real trace: parent `POST /api/auth/refresh` span with the custom `refresh-token.rotate` span nested underneath. All 60 tests across four test projects pass.

## OTel setup

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

```csharp
// Telemetry.cs
internal static class Telemetry
{
    public const string ServiceName = "QuotesApi";
    public static readonly ActivitySource Source = new(ServiceName);
}
```

```csharp
// Correlation middleware, Program.cs
app.Use(async (ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();
```

```csharp
// Custom span, Tokens/RefreshTokenService.cs
using var activity = Telemetry.Source.StartActivity("refresh-token.rotate");
// ...
activity?.SetTag("user.id", stored.UserId);
activity?.SetTag("refresh_token.outcome", "rotated");
```

## Screenshot

`day-4/task-5/trace-screenshot.png` — real trace from Jaeger (http://localhost:16686), service `QuotesApi`, showing the parent `POST /api/auth/refresh` span (743µs) with the child `refresh-token.rotate` span (202µs) nested underneath it, with tags `refresh_token.outcome=rotated` and `user.id` visible.

## What did you learn this session?

The task's claim that logs and traces correlate automatically was false until I checked — `ctx.TraceIdentifier` and OTel's trace ID are entirely different values that would never match.
The Aspire dashboard has no queryable API, so I switched to Jaeger specifically so I could confirm export succeeded through its REST API before trusting a screenshot.

## What would break this?

An uninstrumented dependency shows up as unexplained gap time inside its parent span, and if the collector were down, spans would be dropped silently with no error in the app.
Sampling could mean a specific request was never recorded at all, and trace context not propagating across a service boundary would split one logical request into two disconnected traces.
