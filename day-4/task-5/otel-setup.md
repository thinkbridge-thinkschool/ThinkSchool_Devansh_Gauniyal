# OpenTelemetry setup

## Packages (`QuotesApi.csproj`)

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.17.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.17.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.17.0" />
```

## ActivitySource (`Telemetry.cs`)

```csharp
using System.Diagnostics;

namespace QuotesApi;

internal static class Telemetry
{
    public const string ServiceName = "QuotesApi";

    public static readonly ActivitySource Source = new(ServiceName);
}
```

## Tracing configuration (`Program.cs`)

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

## Correlation fix (`Program.cs`)

```csharp
using System.Diagnostics;
using Serilog.Context;

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

## Custom span (`Tokens/RefreshTokenService.cs`)

```csharp
public TokenPair? Rotate(string? rawToken)
{
    if (string.IsNullOrWhiteSpace(rawToken))
    {
        return null;
    }

    using var activity = Telemetry.Source.StartActivity("refresh-token.rotate");

    lock (_gate)
    {
        // ... existing lookup/expiry/revocation logic ...

        activity?.SetTag("user.id", stored.UserId);
        activity?.SetTag("refresh_token.outcome", "rotated"); // or reuse_detected / already_revoked / not_found_or_expired

        // ...
    }
}
```

## Running a local collector (Jaeger)

```
docker run -d --name jaeger \
  -p 16686:16686 -p 4317:4317 -p 4318:4318 \
  -e COLLECTOR_OTLP_ENABLED=true \
  jaegertracing/all-in-one:1.76.0
```

Dashboard: http://localhost:16686 — search for service `QuotesApi`.
