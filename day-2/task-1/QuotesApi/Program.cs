using QuotesApi.Models;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;
using QuotesApi.Services.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes")));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddTransient<IQuoteValidator, QuoteValidator>();
builder.Services.AddSingleton<IClock, SystemClock>();

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
app.MapPost("/api/quotes", async (
    Quote quote,
    IQuoteRepository repository,
    IQuoteValidator validator) =>
{
    var validationErrors = validator.Validate(quote);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
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
