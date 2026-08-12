using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quotes.Api.Data;
using Quotes.Api.Models;
using Quotes.Api.Time;
using Quotes.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Quotes")));
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var signingKey = configuration["Authentication:SigningKey"]
            ?? throw new InvalidOperationException(
                "Authentication:SigningKey must be configured outside source control.");

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Authentication:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Authentication:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    database.Database.Migrate();
}

app.MapGet("/api/quotes", async (int? limit, QuotesDbContext database) =>
{
    if (limit is <= 0 or > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["limit"] = ["Limit must be between 1 and 100."]
        });
    }

    var query = database.Quotes.AsNoTracking().OrderBy(quote => quote.Id);
    var quotes = limit.HasValue
        ? await query.Take(limit.Value).ToListAsync()
        : await query.ToListAsync();

    return Results.Ok(quotes);
});

app.MapGet("/api/quotes/{id:int}", async (int id, QuotesDbContext database) =>
{
    var quote = await database.Quotes.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id);

    return quote is null ? Results.NotFound() : Results.Ok(quote);
});

app.MapPost("/api/quotes", async (
    CreateQuoteRequest request,
    ClaimsPrincipal user,
    QuotesDbContext database,
    IClock clock) =>
{
    var errors = QuoteRequestValidator.Validate(request.Text);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var quote = new Quote
    {
        OwnerId = user.FindFirstValue("sub")!,
        Text = request.Text!,
        CreatedAtUtc = clock.UtcNow
    };

    database.Quotes.Add(quote);
    await database.SaveChangesAsync();

    return Results.Created($"/api/quotes/{quote.Id}", quote);
}).RequireAuthorization();

app.MapPut("/api/quotes/{id:int}", async (
    int id,
    UpdateQuoteRequest request,
    QuotesDbContext database) =>
{
    var errors = QuoteRequestValidator.Validate(request.Text);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var quote = await database.Quotes.FindAsync(id);
    if (quote is null)
    {
        return Results.NotFound();
    }

    quote.Text = request.Text!;
    await database.SaveChangesAsync();

    return Results.Ok(quote);
}).RequireAuthorization();

app.MapDelete("/api/quotes/{id:int}", async (int id, QuotesDbContext database) =>
{
    var quote = await database.Quotes.FindAsync(id);
    if (quote is null)
    {
        return Results.NotFound();
    }

    database.Quotes.Remove(quote);
    await database.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.Run();

public partial class Program;
