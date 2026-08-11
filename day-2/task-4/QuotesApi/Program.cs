using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes")));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
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
    CreateQuoteRequest request,
    IQuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    var result = Quote.Create(request.Author, request.Text);
    if (!result.IsSuccess)
    {
        var error = result.Error!;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [error.Code] = [error.Message]
        });
    }

    var createdQuote = await repository.CreateAsync(
        result.Value!,
        cancellationToken);

    return Results.Created($"/api/quotes/{createdQuote.Id}", createdQuote);
});
app.MapGet("/api/quotes/{id:int}", async (
    int id,
    IQuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    var quote = await repository.GetByIdAsync(id, cancellationToken);

    return quote is not null
        ? Results.Ok(quote)
        : Results.NotFound();
});
app.MapDelete("/api/quotes/{id:int}", async (
    int id,
    IQuoteRepository repository,
    CancellationToken cancellationToken) =>
{
    var deleted = await repository.DeleteAsync(id, cancellationToken);

    return deleted
        ? Results.NoContent()
        : Results.NotFound();
});
app.Run();
