# Serilog setup

## Packages (`QuotesApi.csproj`)

```xml
<PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="10.0.1" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
```

## Host wiring + correlation middleware (`Program.cs`)

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
});

// ... builder.Services registrations, builder.Build() ...

var app = builder.Build();

// Correlation: every log line written while handling this request shares the same
// TraceId. Registered before routing, authentication and endpoints so nothing
// downstream logs without it attached.
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

## Structured log statements (message templates, never string interpolation)

```csharp
// Login handler
logger.LogInformation("Login attempt received");
logger.LogWarning("Login attempt failed"); // no email/password logged
logger.LogInformation("Login succeeded for user {UserId}", caller.UserId);

// InternalAccessTokenService.Create
_logger.LogInformation(
    "Access token created for user {UserId} with lifetime {LifetimeSeconds}s",
    userId,
    _options.AccessTokenLifetimeSeconds);

// RefreshTokenService.Issue / Rotate
_logger.LogInformation(
    "Refresh token issued for user {UserId} in family {FamilyId}", userId, familyId);
_logger.LogWarning("Refresh token rejected: unknown or expired");
_logger.LogWarning(
    "Refresh token reuse detected; revoking family {FamilyId}", stored.FamilyId);
_logger.LogInformation(
    "Refresh token rotated for user {UserId} in family {FamilyId}",
    stored.UserId, stored.FamilyId);
```

## Log levels (`appsettings.json`)

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

## Dev-only override (`appsettings.Development.json`)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Override": {
        "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
      }
    }
  }
}
```
