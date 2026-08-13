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

The `appsettings.json` I inherited already had a `Logging:LogLevel` section, and it would have kept sitting there doing absolutely nothing the moment Serilog took over — that's a real, easy-to-miss failure mode, not a hypothetical one, since the app would still run and log just fine while that whole section silently stopped mattering. I also learned that testing correlation properly can't rely on capturing real console output, since redirecting `Console.Out` is unsafe once other test classes are running concurrently against their own app instances — the fix was a small DI seam letting a test register its own in-memory sink instead.

## What would break this?

Two things I actually hit while building this: the dead `Logging` section above, and needing to place `UseSerilogRequestLogging()` after the correlation middleware specifically, since registering it first would mean its own summary line logs outside the `TraceId` scope. Beyond that: `ctx.TraceIdentifier` doesn't cross a service boundary on its own (unlike `Activity.Current.TraceId`/W3C trace context), so this correlation only holds within one process; and a Console sink outage (e.g. a blocked or redirected stdout) would silently lose log events with no built-in fallback.
