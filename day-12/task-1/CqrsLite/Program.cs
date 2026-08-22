using CqrsLite;
using CqrsLite.Data;
using CqrsLite.Features.Quotes.Commands;
using CqrsLite.Features.Quotes.Queries;

if (args.Length > 0 && args[0] == "capture-sql")
{
    var captureDbPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "cqrslite.db");
    var captureOutputDir = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "output");
    SqlCaptureRunner.Run(captureDbPath, captureOutputDir);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dbPath = Environment.GetEnvironmentVariable("CQRSLITE_DB_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "cqrslite.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

using (var seedContext = new QuotesDbContext(dbPath))
{
    Seeder.SeedIfNeeded(seedContext);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/quotes", (SubmitQuoteCommand command) =>
{
    using var context = new QuotesDbContext(dbPath);
    var handler = new SubmitQuoteHandler(context);
    var result = handler.Handle(command);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/quotes/wall", () =>
{
    using var context = new QuotesDbContext(dbPath);
    var handler = new QuoteWallHandler(context);
    return Results.Ok(handler.Handle(new QuoteWallQuery()));
});

app.Run();
