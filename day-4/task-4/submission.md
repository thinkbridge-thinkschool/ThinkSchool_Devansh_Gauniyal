# Day 4 — Task 4: Serilog with correlation IDs

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-4/day-4/task-4

## Notes for your mentor

Wired Serilog into the real Day 3 auth API (`day-3/task-3/QuotesApi`), not a demo app — approved before touching it, since it required modifying `Program.cs`, `appsettings.json`, and two token-service files. Correlation middleware pushes `TraceId` via `LogContext` before routing/auth/endpoints. Log levels live under a `Serilog` section (the app's old `Logging` section was dead the moment Serilog took over, so it was removed rather than left misleading). All 58 tests across three test projects pass (19 original + 37 from Task 2 + 2 new correlation tests), and Task 1's CI is unaffected since it only builds an unrelated project.

## Serilog setup

```csharp
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] ({TraceId}) {Message:lj}{NewLine}{Exception}");

    foreach (var sink in services.GetServices<Serilog.Core.ILogEventSink>())
    {
        configuration.WriteTo.Sink(sink);
    }
});

// ... builder.Services registrations, builder.Build() ...

var app = builder.Build();

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

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": { "Microsoft.AspNetCore": "Warning" }
  }
}
```

## 5 lines of structured log output from a single request

```
[14:06:48 INF] (9d7dc20f6c11923ff5b5bb6e0098d8e3) Login attempt received
[14:06:48 INF] (9d7dc20f6c11923ff5b5bb6e0098d8e3) Access token created for user demo-user-1 with lifetime 900s
[14:06:48 INF] (9d7dc20f6c11923ff5b5bb6e0098d8e3) Refresh token issued for user demo-user-1 in family e486a74c-0257-47b4-a5f8-e3c2772dcd41
[14:06:48 INF] (9d7dc20f6c11923ff5b5bb6e0098d8e3) Login succeeded for user demo-user-1
[14:06:48 INF] (9d7dc20f6c11923ff5b5bb6e0098d8e3) HTTP POST /api/auth/login responded 200 in 43.5029 ms
```

## What did you learn this session?

The old `Logging:LogLevel` section would have kept sitting there doing nothing once Serilog took over — an easy, silent failure mode.
Testing correlation can't rely on redirecting real console output, since other tests run concurrently against their own app instances.

## What would break this?

`UseSerilogRequestLogging()` has to sit after the correlation middleware, or its own summary line logs outside the `TraceId` scope.
`ctx.TraceIdentifier` doesn't cross a service boundary on its own, and a Console sink outage would silently lose log events.
