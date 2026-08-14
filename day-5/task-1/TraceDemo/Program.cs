using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using TraceDemo.Data;
using TraceDemo.Queries;

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

const string ServiceName = "TraceDemo";

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
    options.UseSqlite(builder.Configuration.GetConnectionString("TraceDemo")));

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

app.MapGet("/authors/slow", async (AppDbContext db, CancellationToken ct) =>
{
    // BEFORE FIX: deliberate N+1 -- one query for the author list, then one more query
    // per author for that author's books. The trace should show this as many short
    // repeated database spans in sequence.
    var result = await AuthorQueries.GetAuthorsNPlusOneAsync(db, ct);
    return Results.Ok(result);
});

app.MapGet("/authors/sleep", async (AppDbContext db, CancellationToken ct) =>
{
    // BEFORE FIX: Thread.Sleep(1500) exactly as named by the Day 5 Task 1 exercise --
    // simulates a slow synchronous operation unrelated to the database. The trace should
    // show one long request span with no child spans inside it.
    Thread.Sleep(1500);
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
