using SlowApi;

if (args.Length > 0 && args[0] == "diagnostics")
{
    var diagnosticsDbPath = args.Length > 1 ? args[1] : TaskPaths.DefaultDatabasePath();
    var diagnosticsOutputDir = args.Length > 2 ? args[2] : TaskPaths.OutputDirectory();
    DiagnosticsRunner.Run(diagnosticsDbPath, diagnosticsOutputDir);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dbPath = Environment.GetEnvironmentVariable("SLOWAPI_DB_PATH") ?? TaskPaths.DefaultDatabasePath();
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

using (var seedContext = new QuotesDbContext(dbPath))
{
    seedContext.Database.EnsureCreated();
    seedContext.EnableWriteAheadLogging();
    Seeder.SeedIfNeeded(seedContext);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// The deliberately slow endpoint - see AuthorQuoteSummaryQuery for the N+1 and
// missing-index anti-patterns it exercises. Scope is measure-only: no fix belongs here.
app.MapGet("/authors/quote-summary", () =>
{
    using var context = new QuotesDbContext(dbPath);
    var summary = AuthorQuoteSummaryQuery.Run(context);
    return Results.Ok(summary);
});

app.Run();
