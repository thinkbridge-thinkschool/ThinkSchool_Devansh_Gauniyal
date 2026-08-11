using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Auth.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"quotes-api-auth-{Guid.NewGuid():N}.db");

    public string Email { get; } = "integration.user@example.test";
    public string Password { get; } = Convert.ToHexString(
        RandomNumberGenerator.GetBytes(24));
    public string Issuer { get; } = "QuotesApi.Auth.Tests";
    public string Audience { get; } = "QuotesApi.Auth.Tests.Client";
    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Quotes"] = $"Data Source={_databasePath}",
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(SigningKey),
                ["Jwt:AccessTokenLifetimeSeconds"] = "900",
                ["DevelopmentUser:Email"] = Email,
                ["DevelopmentUser:Password"] = Password
            });
        });
    }
}
