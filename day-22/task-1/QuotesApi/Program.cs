using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using QuotesApi;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Caching;
using QuotesApi.Configuration;
using QuotesApi.Performance;
using QuotesApi.Quotes;
using QuotesApi.Resilience;
using QuotesApi.Tokens;
using Serilog;
using Serilog.Context;
using StackExchange.Redis;

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

// Day 21 Task 1: HybridCache (L1 in-process + L2 Redis) in front of the one endpoint
// above that does a real database read (GET /api/authors/quote-summary). See
// PROVENANCE.md and README.md for the full rationale; registration only lives here.
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return configuration.GetSection(MeasurementOptions.SectionName).Get<MeasurementOptions>()
        ?? new MeasurementOptions();
});
builder.Services.AddSingleton<DbQueryCounter>();

// Expiration/LocalCacheExpiration are set explicitly here (see README.md for why:
// 30s total, 10s in-process) rather than left at HybridCache's own defaults.
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(10)
    };
});

// Redis is L2. The connection string is configuration/environment only - never a
// literal here, never committed with real credentials (there are none: local Redis
// has no auth). AbortOnConnectFail=false means a Redis that is down at startup or
// mid-run does not take the app down with it; HybridCache's own default behaviour is
// to catch an L2 failure, log it, and continue serving from L1 alone (verified in
// README.md's "Redis down" section rather than assumed).
var redisConnectionString =
    builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
// Short, explicit timeouts rather than the library's multi-second defaults: this is a
// local single-instance demo, so a slow degrade (each call blocking for several
// seconds before falling back to L1) would make "Redis is down" look like a hang
// instead of a fast, deliberate degrade. Verified empirically (see README.md) - with
// the defaults a cold read against a dead Redis took ~6s; with these it's ~1s.
var redisConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
redisConfigurationOptions.AbortOnConnectFail = false;
redisConfigurationOptions.ConnectTimeout = 1000;
redisConfigurationOptions.ConnectRetry = 1;
redisConfigurationOptions.SyncTimeout = 1000;
redisConfigurationOptions.AsyncTimeout = 1000;

// Day 22 Task 1: Redis's own resilience pipeline (timeout, circuit breaker, bulkhead -
// deliberately no retry, see Resilience/RedisResiliencePipelineConfiguration.cs and
// README.md) sits between HybridCache and the real Redis client. Registered directly
// as IDistributedCache (instead of via AddStackExchangeRedisCache, which Day 21 used)
// because HybridCache resolves whatever IDistributedCache is in the container as its
// L2 - wrapping it here is the one place that makes every L2 call HybridCache already
// makes (Day 21's caching code, completely untouched) transparently go through both
// the resilience pipeline and the fault-injection switch below.
builder.Services.AddKeyedSingleton<FaultInjectionSwitch>(
    DependencyKeys.Redis,
    (_, key) => new FaultInjectionSwitch((string)key!));
builder.Services.AddKeyedSingleton<CircuitBreakerStateProvider>(
    DependencyKeys.Redis,
    (_, _) => new CircuitBreakerStateProvider());
builder.Services.AddSingleton<IDistributedCache>(serviceProvider =>
{
    var innerRedisCache = new RedisCache(
        Options.Create(new RedisCacheOptions
        {
            ConfigurationOptions = redisConfigurationOptions,
            InstanceName = "day21-hybridcache:"
        }));

    var faultSwitch = serviceProvider.GetRequiredKeyedService<FaultInjectionSwitch>(DependencyKeys.Redis);
    var stateProvider = serviceProvider.GetRequiredKeyedService<CircuitBreakerStateProvider>(DependencyKeys.Redis);
    var pipelineLogger = serviceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("QuotesApi.Resilience.Redis");
    var resilienceTuning = serviceProvider.GetRequiredService<IConfiguration>()
        .GetSection(ResilienceTuningOptions.SectionName).Get<ResilienceTuningOptions>()
        ?? new ResilienceTuningOptions();
    var pipeline = RedisResiliencePipelineConfiguration.Build(
        faultSwitch, stateProvider, pipelineLogger, resilienceTuning);

    return new ResilientDistributedCache(innerRedisCache, pipeline, faultSwitch);
});

builder.Services.AddSingleton<AuthorQuoteSummaryCacheService>();

// Day 22 Task 1: the controllable HTTP dependency. Full four-strategy pipeline
// (bulkhead, retry, circuit breaker, timeout - see Resilience/HttpResiliencePipelineConfiguration.cs)
// because GET /api/external/quote-of-the-day is idempotent, unlike a cache write.
builder.Services.AddKeyedSingleton<FaultInjectionSwitch>(
    DependencyKeys.ExternalService,
    (_, key) => new FaultInjectionSwitch((string)key!));
builder.Services.AddKeyedSingleton<CircuitBreakerStateProvider>(
    DependencyKeys.ExternalService,
    (_, _) => new CircuitBreakerStateProvider());
builder.Services.AddSingleton<ExternalServiceClient>();
builder.Services.AddSingleton<ExternalServiceCallCounter>();
// Verification note (see README.md's verification log): this used to fall back to a
// hardcoded "http://localhost:5000" when ExternalService:SelfBaseAddress wasn't set.
// Caught live, not assumed: running the app with ASPNETCORE_URLS pointed at a
// different port (5010, to dodge port 5000 already being held by macOS's AirPlay
// Receiver on this machine) made every "external" call come back a fast, unretried
// 403 - the client was silently calling AirPlay's port, not this app, and AirPlay's
// 403 isn't in the retry strategy's default handled-status set so it never even
// looked like a resilience problem. Fixed by deriving the real default from the same
// "urls" configuration key ASPNETCORE_URLS/--urls populates (read once, before
// builder.Build(), which is early enough), so a self-referencing call always finds
// this same app regardless of which port it's actually bound to.
var configuredUrls = builder.Configuration["urls"];
var firstConfiguredUrl = configuredUrls
    ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .FirstOrDefault();
var selfBaseAddressDefault = firstConfiguredUrl ?? "http://localhost:5000";
builder.Services
    .AddHttpClient(DependencyKeys.ExternalService, client =>
    {
        // The "external service" lives in this same app for convenience (see
        // README.md) - a real one would be a separate process with its own address,
        // configured the same way: environment/config, never a literal here.
        var selfBaseAddress =
            builder.Configuration["ExternalService:SelfBaseAddress"] ?? selfBaseAddressDefault;
        client.BaseAddress = new Uri(selfBaseAddress);
    })
    .AddResilienceHandler(
        HttpResiliencePipelineConfiguration.PipelineName,
        HttpResiliencePipelineConfiguration.Configure);

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

    return Results.Ok(quotes.Create(userId, request.Text, request.Author));
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

// ===== Day 21 Task 1: HybridCache + stampede protection =====
// Everything from here to app.Run() is additive: the endpoint above is untouched.
// A separate lazy-seed guard (not the one above) so these endpoints work standalone
// even if /api/authors/quote-summary above was never called first. EnsureCreated()
// and SeedIfNeeded() are both safely re-runnable, so two independent guards touching
// the same file cause no harm.
var cachingSeedLock = new object();
var cachingDbSeeded = false;

// Verification note (see README.md / PROVENANCE.md verification log): this used to
// have an extra "if (cachingDbSeeded) return;" check *before* the lock, mirroring the
// original endpoint's pattern above. Under a genuine 40-way concurrent burst against a
// fresh performance.db (QuotesApi.Tests.CachingTests), that pre-lock read of a plain
// bool - with no volatile/memory-barrier guarantee - let more than one caller past it
// before the flag's write had become visible, so more than one thread reached
// EnsureCreated() and failed with "table Authors already exists". Removing the
// pre-lock check and always taking the lock first (cheap once the one-time seed is
// done) fixes it: every read of cachingDbSeeded now happens under the same lock that
// guards its write, so it has a real happens-before relationship.
void EnsurePerformanceDbSeededForCaching()
{
    lock (cachingSeedLock)
    {
        if (cachingDbSeeded)
        {
            return;
        }

        using var seedContext = new PerformanceDbContext(performanceDbPath);
        seedContext.Database.EnsureCreated();
        seedContext.EnableWriteAheadLogging();
        PerformanceSeeder.SeedIfNeeded(seedContext);
        cachingDbSeeded = true;
    }
}

app.MapGet("/api/authors/quote-summary/cached", async (
    string? key,
    AuthorQuoteSummaryCacheService cacheService,
    CancellationToken cancellationToken) =>
{
    EnsurePerformanceDbSeededForCaching();
    var stopwatch = Stopwatch.StartNew();
    var summary = await cacheService.GetSummaryAsync(performanceDbPath, key ?? "default", cancellationToken);
    stopwatch.Stop();
    return Results.Ok(new { summary, elapsedMs = stopwatch.Elapsed.TotalMilliseconds });
});

// Deliberate measurement baseline, not production code: same query, same artificial
// delay as the cached endpoint above, with no cache at all - so the before/after
// comparison is a real measurement rather than a claim.
app.MapGet("/api/authors/quote-summary/uncached", async (
    DbQueryCounter counter,
    MeasurementOptions measurementOptions,
    CancellationToken cancellationToken) =>
{
    EnsurePerformanceDbSeededForCaching();
    var stopwatch = Stopwatch.StartNew();
    var summary = await AuthorQuoteSummaryReader.ReadAsync(
        performanceDbPath, counter, measurementOptions, cancellationToken);
    stopwatch.Stop();
    return Results.Ok(new { summary, elapsedMs = stopwatch.Elapsed.TotalMilliseconds });
});

app.MapGet("/api/measurement/db-query-count", (DbQueryCounter counter) =>
    Results.Ok(new { count = counter.Count }));

// Resets the counter AND evicts every cached summary (by tag), so a demo run or a
// test can always start from a genuinely cold key rather than one still warm from a
// previous run.
app.MapPost("/api/measurement/reset", async (
    DbQueryCounter counter,
    AuthorQuoteSummaryCacheService cacheService,
    CancellationToken cancellationToken) =>
{
    counter.Reset();
    await cacheService.EvictAsync(cancellationToken);
    return Results.Ok(new { count = counter.Count });
});

// ===== Day 22 Task 1: Resilience with Polly =====
// Everything from here to app.UseDefaultFiles() is additive; nothing above (Day 21's
// caching endpoints, or the original /api/authors/quote-summary) is touched.

// The controllable "external service" itself - the dependency being called through
// the resilient client below. TEST/DEMO SCAFFOLDING: in a real system this would be a
// separate process with its own address (see README.md); here it lives in this same
// app for convenience. Its health is driven entirely by the "external-service"
// fault-injection switch, never by anything real.
app.MapGet("/api/external/quote-of-the-day", async (
    [FromKeyedServices(DependencyKeys.ExternalService)] FaultInjectionSwitch faultSwitch,
    ExternalServiceCallCounter callCounter,
    CancellationToken cancellationToken) =>
{
    // Counts a REAL invocation of the dependency itself - the concrete evidence
    // (Phase 7) that an open breaker or a bulkhead rejection short-circuits before
    // ever reaching here, and that retry really did attempt the configured number of
    // times. Incremented unconditionally, before the fault check, so even an
    // injected-failure response still counts as "the dependency was called".
    callCounter.Increment();

    if (faultSwitch.Mode == FaultMode.Failing)
    {
        return Results.Problem(
            detail: "Injected failure (fault-injection switch set to Failing).",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (faultSwitch.Mode == FaultMode.Slow)
    {
        await Task.Delay(faultSwitch.SlowDelay, cancellationToken);
    }

    return Results.Ok(new
    {
        quote = "The only way to do great work is to love what you do.",
        source = "external"
    });
});

app.MapGet("/api/resilience/external/call-count", (ExternalServiceCallCounter callCounter) =>
    Results.Ok(new { count = callCounter.Count }));

app.MapPost("/api/resilience/external/call-count/reset", (ExternalServiceCallCounter callCounter) =>
{
    callCounter.Reset();
    return Results.Ok(new { count = callCounter.Count });
});

// Fires one call through the resilience-wrapped ExternalServiceClient - what the demo
// page's "fire N requests" button calls, N times, to exercise retry, the circuit
// breaker, and the bulkhead together, through real use rather than direct pipeline
// manipulation.
app.MapGet("/api/resilience/external/call", async (
    ExternalServiceClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var body = await client.GetQuoteOfTheDayAsync(cancellationToken);
        return Results.Ok(new { outcome = "success", body });
    }
    catch (BrokenCircuitException)
    {
        return Results.Ok(new { outcome = "short-circuited" });
    }
    catch (RateLimiterRejectedException)
    {
        return Results.Ok(new { outcome = "bulkhead-rejected" });
    }
    catch (Exception exception)
    {
        return Results.Ok(new { outcome = "failed", error = exception.GetType().Name });
    }
});

// Fires one call straight at the resilience-wrapped Redis IDistributedCache, bypassing
// HybridCache and the database entirely, so the Redis breaker's own lifecycle can be
// driven and observed in isolation from the cached endpoint's DB-fallback behaviour.
app.MapGet("/api/resilience/redis/call", async (
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    try
    {
        await cache.SetAsync(
            "resilience-demo-key",
            "ping"u8.ToArray(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
            cancellationToken);
        var value = await cache.GetAsync("resilience-demo-key", cancellationToken);
        return Results.Ok(new
        {
            outcome = "success",
            value = value is null ? null : System.Text.Encoding.UTF8.GetString(value)
        });
    }
    catch (BrokenCircuitException)
    {
        return Results.Ok(new { outcome = "short-circuited" });
    }
    catch (RateLimiterRejectedException)
    {
        return Results.Ok(new { outcome = "bulkhead-rejected" });
    }
    catch (Exception exception)
    {
        return Results.Ok(new { outcome = "failed", error = exception.GetType().Name });
    }
});

// Fault-injection controls (TEST/DEMO SCAFFOLDING, see Resilience/FaultMode.cs):
// {dependency} is "redis" or "external-service".
app.MapPost("/api/faults/{dependency}", (
    string dependency,
    string mode,
    IServiceProvider services) =>
{
    var faultSwitch = ResolveFaultSwitch(services, dependency);
    if (faultSwitch is null)
    {
        return Results.NotFound(new { error = $"Unknown dependency '{dependency}'." });
    }

    if (!Enum.TryParse<FaultMode>(mode, ignoreCase: true, out var parsedMode))
    {
        return Results.BadRequest(new { error = $"Unknown mode '{mode}'. Use healthy, failing, or slow." });
    }

    faultSwitch.Mode = parsedMode;
    return Results.Ok(new { dependency, mode = parsedMode.ToString() });
});

app.MapGet("/api/faults/{dependency}", (string dependency, IServiceProvider services) =>
{
    var faultSwitch = ResolveFaultSwitch(services, dependency);
    return faultSwitch is null
        ? Results.NotFound(new { error = $"Unknown dependency '{dependency}'." })
        : Results.Ok(new { dependency, mode = faultSwitch.Mode.ToString() });
});

static FaultInjectionSwitch? ResolveFaultSwitch(IServiceProvider services, string dependency) => dependency switch
{
    DependencyKeys.Redis => services.GetRequiredKeyedService<FaultInjectionSwitch>(DependencyKeys.Redis),
    DependencyKeys.ExternalService =>
        services.GetRequiredKeyedService<FaultInjectionSwitch>(DependencyKeys.ExternalService),
    _ => null
};

// Live breaker state - for the demo page's live display and for tests. Resolving
// IDistributedCache here (rather than only via the keyed CircuitBreakerStateProvider)
// forces the Redis pipeline to have been built at least once, so CircuitState is
// always readable even if this is the very first call this process has made.
app.MapGet("/api/resilience/breakers", (
    IDistributedCache _,
    ExternalServiceClient __,
    IServiceProvider services) =>
{
    var redisState = services
        .GetRequiredKeyedService<CircuitBreakerStateProvider>(DependencyKeys.Redis).CircuitState;
    var externalState = services
        .GetRequiredKeyedService<CircuitBreakerStateProvider>(DependencyKeys.ExternalService).CircuitState;
    return Results.Ok(new { redis = redisState.ToString(), externalService = externalState.ToString() });
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

static string? GetUserId(ClaimsPrincipal user) =>
    user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

public partial class Program;
