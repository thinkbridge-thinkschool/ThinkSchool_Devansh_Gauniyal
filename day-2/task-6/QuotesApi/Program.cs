using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services.Auth;
using QuotesApi.Services.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes")));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(JwtOptions.SectionName)
        .Get<JwtOptions>() ?? new JwtOptions();
    options.ValidateAndGetSigningKey();
    return options;
});
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<JwtOptions>()
        .ValidateAndGetSigningKey());
builder.Services.AddSingleton<JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtOptions>((options, jwtOptions) =>
    {
        var signingKey = jwtOptions.ValidateAndGetSigningKey();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
_ = app.Services.GetRequiredService<JwtOptions>();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.MigrateAsync();

    var developmentEmail = builder.Configuration["DevelopmentUser:Email"];
    var developmentPassword = builder.Configuration["DevelopmentUser:Password"];
    if (!string.IsNullOrWhiteSpace(developmentEmail) ||
        !string.IsNullOrWhiteSpace(developmentPassword))
    {
        if (string.IsNullOrWhiteSpace(developmentEmail) ||
            string.IsNullOrWhiteSpace(developmentPassword))
        {
            throw new InvalidOperationException(
                "Development user email and password must both be configured.");
        }

        var normalizedEmail = User.NormalizeEmail(developmentEmail);
        var userExists = await db.Users.AnyAsync(user =>
            user.Email == normalizedEmail);
        if (!userExists)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(developmentPassword);
            db.Users.Add(User.Create(normalizedEmail, passwordHash));
            await db.SaveChangesAsync();
        }
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Quotes API is running");

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    QuotesDbContext db,
    JwtTokenService tokenService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Unauthorized();
    }

    var normalizedEmail = User.NormalizeEmail(request.Email);
    var user = await db.Users.SingleOrDefaultAsync(
        value => value.Email == normalizedEmail,
        cancellationToken);

    if (user is null ||
        !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(tokenService.Issue(user));
});

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

    return Results.Ok(createdQuote);
}).RequireAuthorization();

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
}).RequireAuthorization();

app.Run();

public partial class Program;
