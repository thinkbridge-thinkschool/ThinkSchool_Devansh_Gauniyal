using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using QuotesApi.Data;
using QuotesApi.Queries;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] ({TraceId}) {Message:lj}{NewLine}{Exception}");
});

const string ServiceName = "QuotesApi";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        // AddOtlpExporter() reads OTEL_EXPORTER_OTLP_ENDPOINT itself, defaulting to
        // http://localhost:4317 (OTLP/gRPC) when the variable is unset -- this is how
        // traces reach the locally running Jaeger instance.
        .AddOtlpExporter());

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("QuotesApi")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    SeedData.EnsureSeeded(db);
}

// Correlation: push the real OpenTelemetry trace ID onto every log line for this
// request, so a slow trace in Jaeger can be cross-referenced with its console logs.
app.Use(async (ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();

// Proves two things at once: OSArchitecture shows whether this process is genuinely
// running arm64 or is being emulated, and MachineName shows a container-generated
// hostname (not the Mac's), proving the response came from inside the container.
app.MapGet("/", () => Results.Ok(new { message = "QuotesApi is running." }));

app.MapGet("/health", () => Results.Ok(new
{
    service = ServiceName,
    machineName = Environment.MachineName,
    architecture = RuntimeInformation.OSArchitecture.ToString(),
    utc = DateTime.UtcNow
}));

app.MapGet("/authors/slow", async (AppDbContext db, CancellationToken ct) =>
{
    // FIXED: was a deliberate N+1 -- one query per author (31 round trips, 66.07ms in the
    // traced run). Now calls the single-query method, same as /authors/fast.
    // GetAuthorsNPlusOneAsync is kept in AuthorQueries as the regression-test fixture that
    // proves the round-trip-counting test would catch a slide back to N+1.
    var result = await AuthorQueries.GetAuthorsSingleQueryAsync(db, ct);
    return Results.Ok(result);
});

app.MapGet("/authors/sleep", async (AppDbContext db, CancellationToken ct) =>
{
    // FIXED: the Thread.Sleep(1500) named by the Day 5 Task 1 exercise (1.54s in the
    // traced run, with no child spans under it) has been removed.
    var result = await AuthorQueries.GetAuthorsSingleQueryAsync(db, ct);
    return Results.Ok(result);
});

app.MapGet("/authors/fast", async (AppDbContext db, CancellationToken ct) =>
{
    // Control endpoint: always efficient, never sleeps. Baseline for the trace comparison.
    var result = await AuthorQueries.GetAuthorsSingleQueryAsync(db, ct);
    return Results.Ok(result);
});

app.Run();
