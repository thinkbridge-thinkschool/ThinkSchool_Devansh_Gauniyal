using FastApi;

// Standalone diagnostics pass (no web host at all), triggered via a CLI argument, exactly
// as in task-1. Captures the single-request SQL log, EXPLAIN QUERY PLAN, and schema dump
// for whichever fixed variant is named.
if (args.Length > 0 && args[0] == "diagnostics")
{
    var variant = args.Length > 1 ? args[1] : "projection";
    var diagnosticsDbPath = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "fastapi.db");
    var diagnosticsOutputDir = args.Length > 3 ? args[3] : Path.Combine(AppContext.BaseDirectory, "diagnostics-output");
    DiagnosticsRunner.Run(variant, diagnosticsDbPath, diagnosticsOutputDir);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dbPath = Environment.GetEnvironmentVariable("FASTAPI_DB_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "fastapi.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

using (var seedContext = new QuotesDbContext(dbPath))
{
    seedContext.Database.EnsureCreated();
    seedContext.EnableWriteAheadLogging();
    Seeder.SeedIfNeeded(seedContext);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Reproduced slow endpoint (task-1's exact N+1) - kept for in-process comparison, not
// load-tested again. Same URL shape as task-1's baseline route, minus the /api prefix
// task-1's project didn't need for its other two variants either.
app.MapGet("/authors/quote-summary/slow", () =>
{
    using var context = new QuotesDbContext(dbPath);
    return Results.Ok(Queries.RunSlow(context));
});

// PRIMARY fixed endpoint - projection. Same relative path task-1 used
// (/api/authors/quote-summary) so the headline before/after comparison is against the
// identical route shape.
app.MapGet("/api/authors/quote-summary", () =>
{
    using var context = new QuotesDbContext(dbPath);
    return Results.Ok(Queries.RunProjection(context));
});

// SECOND fixed variant - Include with split queries.
app.MapGet("/authors/quote-summary/split", () =>
{
    using var context = new QuotesDbContext(dbPath);
    return Results.Ok(Queries.RunSplitQuery(context));
});

app.Run();
