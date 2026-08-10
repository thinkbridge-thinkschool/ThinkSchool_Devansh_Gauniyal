using QuotesApi.Models;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite("Data Source=quotes.db"));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    db.Database.Migrate();
}

app.MapGet("/", () => "Quotes API is running");
app.MapGet("/api/quotes", async (
    int page,
    int size,
    IQuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    var quotes = await repository.GetAllAsync(page, size, cancellationToken);
    return Results.Ok(quotes);
});
app.MapPost("/api/quotes", async (Quote quote, IQuoteRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(quote.Author) ||
        string.IsNullOrWhiteSpace(quote.Text))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["quote"] = ["Author and text are required."]
        });
    }

    var createdQuote = await repository.CreateAsync(quote);

    return Results.Created($"/api/quotes/{createdQuote.Id}", createdQuote);
});
app.MapGet("/api/quotes/{id:int}", async (int id, IQuoteRepository repository) =>
{
    var quote = await repository.GetByIdAsync(id);

    return quote is not null
        ? Results.Ok(quote)
        : Results.NotFound();
});
app.MapDelete("/api/quotes/{id:int}", async (int id, IQuoteRepository repository) =>
{
    var deleted = await repository.DeleteAsync(id);

    return deleted
        ? Results.NoContent()
        : Results.NotFound();
});
app.Run();