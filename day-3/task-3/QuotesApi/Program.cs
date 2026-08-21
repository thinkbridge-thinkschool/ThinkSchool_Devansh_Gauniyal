using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Configuration;
using QuotesApi.Performance;
using QuotesApi.Quotes;
using QuotesApi.Tokens;
using Serilog;
using Serilog.Context;

// Day 11 Task 1: a standalone diagnostics pass (build no web host at all) that captures
// the SQL log, EXPLAIN QUERY PLAN and schema dump for the deliberately slow performance
// endpoint below. Kept entirely separate from app startup so it never runs unless
// explicitly invoked this way.
if (args.Length > 0 && args[0] == "performance-diagnostics")
{
    var diagnosticsDbPath = args.Length > 1
        ? args[1]
        : Path.Combine(AppContext.BaseDirectory, "performance.db");
    var diagnosticsOutputDir = args.Length > 2
        ? args[2]
        : Path.Combine(AppContext.BaseDirectory, "performance-output");
    PerformanceDiagnosticsRunner.Run(diagnosticsDbPath, diagnosticsOutputDir);
    return;
}

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

// Exporter decision (Day 4 Task 6): OTLP-to-local-Jaeger stays a Development-only
// convenience; Azure Monitor is the exporter for every other real environment. Never
// both at once, and never anything in "Testing" -- see the class-level comments below
// on each branch for why.
var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName));

openTelemetryBuilder.WithTracing(tracing =>
{
    tracing
        .AddSource(Telemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();

    // No collector is reachable in "Testing" (every WebApplicationFactory-hosted test
    // uses it) so no exporter at all is added there -- AddSource/instrumentation stay
    // registered so Activities are still genuinely created and sampled (tests assert on
    // them via a raw ActivityListener), just never sent anywhere over the network.
    if (builder.Environment.IsDevelopment())
    {
        // AddOtlpExporter() reads the standard OTEL_EXPORTER_OTLP_ENDPOINT environment
        // variable itself, defaulting to http://localhost:4317 when unset.
        tracing.AddOtlpExporter();
    }
});

if (!builder.Environment.IsEnvironment("Testing") && !builder.Environment.IsDevelopment())
{
    var appInsightsConnectionString = ResolveAppInsightsConnectionString(builder.Configuration);
    openTelemetryBuilder.UseAzureMonitor(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
    });
}

// Resolves the App Insights connection string from Key Vault using DefaultAzureCredential
// (the local `az login` identity locally, a managed identity when deployed) -- never from
// appsettings, a code literal, or an environment variable holding the string itself. Only
// the Key Vault NAME is configuration, via "KeyVault:Name".
static string ResolveAppInsightsConnectionString(IConfiguration configuration)
{
    const string ConnectionStringSecretName = "ApplicationInsights-ConnectionString";

    var vaultName = configuration["KeyVault:Name"];
    if (string.IsNullOrWhiteSpace(vaultName))
    {
        throw new InvalidOperationException(
            "KeyVault:Name must be configured to resolve the Application Insights " +
            "connection string outside the Development/Testing environments.");
    }

    // Opt-in escape hatch, off by default: on a real Azure VM/App Service deployment,
    // ManagedIdentityCredential's IMDS probe succeeds in milliseconds, so this stays
    // false there and managed identity keeps working exactly as before. Off Azure, a
    // dropped (rather than refused) probe to 169.254.169.254 makes Azure.Identity
    // classify the timeout as a fatal AuthenticationFailedException instead of "try
    // the next credential", which otherwise stops DefaultAzureCredential from ever
    // reaching AzureCliCredential during local verification.
    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential =
            configuration.GetValue<bool>("KeyVault:ExcludeManagedIdentityCredential")
    };

    var client = new SecretClient(
        new Uri($"https://{vaultName}.vault.azure.net/"),
        new DefaultAzureCredential(credentialOptions));

    try
    {
        // One-time synchronous call during startup bootstrap, not per-request code.
        return client.GetSecret(ConnectionStringSecretName).Value.Value;
    }
    catch (Exception exception) when (exception is not InvalidOperationException)
    {
        // Deliberately not including exception.Message or the exception itself: fail
        // loudly with a generic, safe message instead of ever risking a Key Vault
        // error detail -- or worse, a partially-read secret -- reaching a log sink.
        // The alternative (starting up with telemetry silently disabled) is worse:
        // it would hide a real misconfiguration behind an app that looks healthy.
        throw new InvalidOperationException(
            "Failed to retrieve the Application Insights connection string from Key " +
            "Vault. Check that the vault is reachable, DefaultAzureCredential can " +
            "authenticate, and the secret exists.");
    }
}

// Day 4 Task 7: the real IOptions pattern -- bind the "InternalJwt" section into
// InternalJwtOptions, and fail startup loudly (ValidateOnStart) rather than let a
// missing/malformed signing key or lifetime surface later as a broken token. The
// Validate delegate reuses ValidateAndGetSigningKey() as the single source of truth
// for what "valid" means, instead of duplicating those rules here.
builder.Services
    .AddOptions<InternalJwtOptions>()
    .Bind(builder.Configuration.GetSection(InternalJwtOptions.SectionName))
    .Validate(
        options =>
        {
            options.ValidateAndGetSigningKey();
            return true;
        })
    .ValidateOnStart();

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
    .Configure<IOptions<InternalJwtOptions>>((options, internalJwtOptions) =>
    {
        var internalJwt = internalJwtOptions.Value;
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
// InternalJwtOptions no longer needs an eager GetRequiredService call here --
// ValidateOnStart() (registered above) already validates it as part of app startup.
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

// Day 11 Task 1: a deliberately slow endpoint reproducing an N+1 query over a missing FK
// index (see QuotesApi.Performance). Additive only - nothing above this point changed.
// Scope is measure-only: this stays unfixed. Seeding is lazy (first request only) so
// every other test that boots this app via WebApplicationFactory is completely
// unaffected unless it actually calls this endpoint.
var performanceDbPath = Environment.GetEnvironmentVariable("PERFORMANCE_DB_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "performance.db");
var performanceSeeded = false;
var performanceSeedLock = new object();

app.MapGet("/api/authors/quote-summary", () =>
{
    if (!performanceSeeded)
    {
        lock (performanceSeedLock)
        {
            if (!performanceSeeded)
            {
                using var seedContext = new PerformanceDbContext(performanceDbPath);
                seedContext.Database.EnsureCreated();
                seedContext.EnableWriteAheadLogging();
                PerformanceSeeder.SeedIfNeeded(seedContext);
                performanceSeeded = true;
            }
        }
    }

    using var context = new PerformanceDbContext(performanceDbPath);
    var summary = AuthorQuoteSummaryQuery.Run(context);
    return Results.Ok(summary);
});

app.Run();

static string? GetUserId(ClaimsPrincipal user) =>
    user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

public partial class Program;
