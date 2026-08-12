using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using EntraAuthApi;
using EntraAuthApi.Authorization;
using EntraAuthApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(InternalJwtOptions.SectionName)
        .Get<InternalJwtOptions>() ?? new InternalJwtOptions();
    options.ValidateAndGetSigningKey();
    return options;
});
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(EntraOptions.SectionName)
        .Get<EntraOptions>() ?? new EntraOptions();
    options.ValidateAndGetAuthority();
    return options;
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = AuthenticationSchemes.SmartBearer;
        options.DefaultChallengeScheme = AuthenticationSchemes.SmartBearer;
    })
    .AddPolicyScheme(
        AuthenticationSchemes.SmartBearer,
        displayName: null,
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var entraOptions = context.RequestServices
                    .GetRequiredService<EntraOptions>();
                var entraAuthority = entraOptions.ValidateAndGetAuthority();
                var authorization = context.Request.Headers.Authorization.ToString();
                const string bearerPrefix = "Bearer ";

                if (authorization.StartsWith(
                        bearerPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var token = authorization[bearerPrefix.Length..].Trim();
                    var handler = new JwtSecurityTokenHandler();

                    if (handler.CanReadToken(token))
                    {
                        try
                        {
                            var issuer = handler.ReadJwtToken(token).Issuer;
                            if (string.Equals(
                                    issuer,
                                    entraAuthority,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return AuthenticationSchemes.EntraId;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // The selected JWT handler will reject the malformed token.
                        }
                    }
                }

                return AuthenticationSchemes.InternalJwt;
            };
        })
    .AddJwtBearer(AuthenticationSchemes.InternalJwt)
    .AddJwtBearer(AuthenticationSchemes.EntraId);

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.InternalJwt)
    .Configure<InternalJwtOptions>((options, internalJwt) =>
    {
        var internalSigningKey = internalJwt.ValidateAndGetSigningKey();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = internalJwt.Issuer,
            ValidateAudience = true,
            ValidAudience = internalJwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(internalSigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.EntraId)
    .Configure<EntraOptions>((options, entra) =>
    {
        var entraAuthority = entra.ValidateAndGetAuthority();
        options.Authority = entraAuthority;
        options.Audience = entra.Audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = entra.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    });

builder.Services.AddSingleton<IQuoteRepository, InMemoryQuoteRepository>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnQuoteAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.CanEditQuotes,
        policy => policy.RequireClaim("scope", "quotes.write"));

    options.AddPolicy(
        AuthorizationPolicies.CanDeleteOwnQuote,
        policy => policy.AddRequirements(new OwnQuoteRequirement()));
});

var app = builder.Build();
_ = app.Services.GetRequiredService<InternalJwtOptions>();
_ = app.Services.GetRequiredService<EntraOptions>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { message = "Entra authentication API is running." }));

app.MapGet("/api/protected", () =>
    Results.Ok(new { message = "Authentication succeeded." }))
    .RequireAuthorization();

app.MapPut("/api/quotes/{id:int}", (int id, QuoteUpdateRequest request) =>
    Results.Ok(new { id, text = request.Text }))
    .RequireAuthorization(AuthorizationPolicies.CanEditQuotes);

app.MapDelete(
    "/api/quotes/{id:int}",
    async (
        int id,
        IQuoteRepository quotes,
        IAuthorizationService authorization,
        ClaimsPrincipal user) =>
    {
        var quote = quotes.Find(id);
        if (quote is null)
        {
            return Results.NotFound();
        }

        var result = await authorization.AuthorizeAsync(
            user,
            quote,
            AuthorizationPolicies.CanDeleteOwnQuote);

        return result.Succeeded
            ? Results.NoContent()
            : Results.Forbid();
    })
    .RequireAuthorization();

app.Run();

public sealed record QuoteUpdateRequest(string Text);

public partial class Program;
