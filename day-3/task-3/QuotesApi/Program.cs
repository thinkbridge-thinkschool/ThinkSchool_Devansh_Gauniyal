using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Configuration;
using QuotesApi.Quotes;
using QuotesApi.Tokens;
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

    // Test seam only: lets a WebApplicationFactory-hosted test register an in-memory
    // Serilog.Core.ILogEventSink via DI to assert on emitted log events (e.g. that a
    // request's log lines share a TraceId), without touching real console output.
    // No sink is registered in production, so this is a no-op outside of tests.
    foreach (var sink in services.GetServices<Serilog.Core.ILogEventSink>())
    {
        configuration.WriteTo.Sink(sink);
    }
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(Telemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        // No collector is reachable in the "Testing" environment (every
        // WebApplicationFactory-hosted test uses it), so the OTLP exporter is skipped
        // there entirely -- otherwise tests would try to connect to a nonexistent
        // localhost:4317 collector on every run. AddOtlpExporter() otherwise reads the
        // standard OTEL_EXPORTER_OTLP_ENDPOINT environment variable itself, defaulting
        // to http://localhost:4317 when unset.
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            tracing.AddOtlpExporter();
        }
    });

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(InternalJwtOptions.SectionName)
        .Get<InternalJwtOptions>() ?? new InternalJwtOptions();
    options.ValidateAndGetSigningKey();
    return options;
});
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(EntraOptions.SectionName)
        .Get<EntraOptions>() ?? new EntraOptions();
    options.ValidateAndGetAuthority();
    return options;
});
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(InternalCallerOptions.SectionName)
        .Get<InternalCallerOptions>() ?? new InternalCallerOptions();
    options.Validate();
    return options;
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = AuthenticationSchemes.SmartBearer;
        options.DefaultChallengeScheme = AuthenticationSchemes.SmartBearer;
    })
    .AddPolicyScheme(
        AuthenticationSchemes.SmartBearer,
        displayName: null,
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var entra = context.RequestServices.GetRequiredService<EntraOptions>();
                var entraAuthority = entra.ValidateAndGetAuthority();
                var authorization = context.Request.Headers.Authorization.ToString();
                const string bearerPrefix = "Bearer ";

                if (authorization.StartsWith(
                        bearerPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var rawToken = authorization[bearerPrefix.Length..].Trim();
                    var handler = new JwtSecurityTokenHandler();

                    if (handler.CanReadToken(rawToken))
                    {
                        try
                        {
                            var issuer = handler.ReadJwtToken(rawToken).Issuer;
                            if (string.Equals(
                                    issuer,
                                    entraAuthority,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return AuthenticationSchemes.EntraId;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // The selected JWT handler will reject malformed tokens.
                        }
                    }
                }

                return AuthenticationSchemes.InternalJwt;
            };
        })
    .AddJwtBearer(AuthenticationSchemes.InternalJwt)
    .AddJwtBearer(AuthenticationSchemes.EntraId);

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.InternalJwt)
    .Configure<InternalJwtOptions>((options, internalJwt) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = internalJwt.Issuer,
            ValidateAudience = true,
            ValidAudience = internalJwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                internalJwt.ValidateAndGetSigningKey()),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.EntraId)
    .Configure<EntraOptions>((options, entra) =>
    {
        options.Authority = entra.ValidateAndGetAuthority();
        options.Audience = entra.Audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = entra.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InternalAccessTokenService>();
builder.Services.AddSingleton<RefreshTokenService>();
builder.Services.AddSingleton<IQuoteRepository, InMemoryQuoteRepository>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnQuoteAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.CanEditQuotes,
        policy => policy.RequireClaim("scope", "quotes.write"));

    options.AddPolicy(
        AuthorizationPolicies.CanDeleteOwnQuote,
        policy => policy.AddRequirements(new OwnQuoteRequirement()));
});

var app = builder.Build();
_ = app.Services.GetRequiredService<InternalJwtOptions>();
_ = app.Services.GetRequiredService<EntraOptions>();
_ = app.Services.GetRequiredService<InternalCallerOptions>();

// Correlation: every log line written while handling this request shares the same
// TraceId, so a mentor (or ReportGenerator, or App Insights/KQL later) can pull every
// log line for one request by filtering on this property. Registered before routing,
// authentication and endpoints so nothing downstream logs without it attached.
//
// Prefers the real OpenTelemetry W3C trace ID (Activity.Current?.TraceId) so this value
// genuinely matches the TraceId on the exported span -- ctx.TraceIdentifier is ASP.NET
// Core's own unrelated per-request identifier and would never match an OTel trace ID.
// Falls back to TraceIdentifier only if no Activity is active for some reason (e.g. if
// AspNetCore instrumentation were ever removed).
app.Use(async (ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { message = "Quotes API is running." }));

app.MapGet("/api/protected", () =>
    Results.Ok(new { message = "Authentication succeeded." }))
    .RequireAuthorization();

// Login and refresh intentionally validate credentials/tokens instead of quote policies.
app.MapPost("/api/auth/login", (
    LoginRequest request,
    InternalCallerOptions caller,
    RefreshTokenService tokens,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Login attempt received");

    if (string.IsNullOrWhiteSpace(request.Email)
        || string.IsNullOrWhiteSpace(request.Password)
        || !string.Equals(request.Email, caller.Email, StringComparison.OrdinalIgnoreCase)
        || !caller.PasswordMatches(request.Password))
    {
        // Deliberately not logging the submitted email: it's unvalidated input, logging it
        // would let an attacker use logs to enumerate which addresses look "close" to real,
        // and it isn't needed to explain what happened (the caller is the single configured
        // internal account either way).
        logger.LogWarning("Login attempt failed");
        return Results.Unauthorized();
    }

    var issued = tokens.Issue(caller.UserId!, caller.Email!);
    logger.LogInformation("Login succeeded for user {UserId}", caller.UserId);
    return Results.Ok(issued);
});

app.MapPost("/api/auth/refresh", (
    RefreshRequest request,
    RefreshTokenService tokens) =>
{
    var pair = tokens.Rotate(request.RefreshToken);
    return pair is null
        ? Results.Unauthorized()
        : Results.Ok(pair);
});

app.MapGet("/api/quotes", (IQuoteRepository quotes) =>
    Results.Ok(quotes.GetAll()));

app.MapPost("/api/quotes", (
    CreateQuoteRequest request,
    IQuoteRepository quotes,
    ClaimsPrincipal user) =>
{
    var userId = GetUserId(user);
    if (userId is null)
    {
        return Results.Forbid();
    }

    return Results.Ok(quotes.Create(userId, request.Text));
}).RequireAuthorization(AuthorizationPolicies.CanEditQuotes);

app.MapPut("/api/quotes/{id:int}", (
    int id,
    UpdateQuoteRequest request,
    IQuoteRepository quotes) =>
{
    var updated = quotes.Update(id, request.Text);
    return updated is null
        ? Results.NotFound()
        : Results.Ok(updated);
}).RequireAuthorization(AuthorizationPolicies.CanEditQuotes);

app.MapDelete("/api/quotes/{id:int}", async (
    int id,
    IQuoteRepository quotes,
    IAuthorizationService authorization,
    ClaimsPrincipal user) =>
{
    var quote = quotes.Find(id);
    if (quote is null)
    {
        return Results.NotFound();
    }

    var result = await authorization.AuthorizeAsync(
        user,
        quote,
        AuthorizationPolicies.CanDeleteOwnQuote);

    if (!result.Succeeded)
    {
        return Results.Forbid();
    }

    quotes.Delete(id);
    return Results.Ok(new { deleted = id });
}).RequireAuthorization();

app.Run();

static string? GetUserId(ClaimsPrincipal user) =>
    user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

public partial class Program;
