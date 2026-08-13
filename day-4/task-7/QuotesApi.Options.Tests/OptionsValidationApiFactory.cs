using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Options.Tests;

/// <summary>
/// Configures every setting InternalJwtOptions.ValidateAndGetSigningKey() checks so the
/// caller can omit or corrupt exactly one, and see the app fail to start for that reason
/// alone -- proving ValidateOnStart() genuinely runs during host startup, not just when
/// something happens to read IOptions&lt;InternalJwtOptions&gt; later.
/// </summary>
public sealed class OptionsValidationApiFactory : WebApplicationFactory<Program>
{
    public string? SigningKeyBase64Override { get; set; } =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string? AccessTokenLifetimeOverride { get; set; } = "00:15:00";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes("test-password-not-real"),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);

            var values = new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "quotes-api.options-tests",
                ["InternalJwt:Audience"] = "quotes-api.options-test-clients",
                ["Entra:TenantId"] = Guid.NewGuid().ToString(),
                ["Entra:Audience"] = "quotes-api.options-tests-entra-audience",
                ["InternalCaller:UserId"] = "options-test-user",
                ["InternalCaller:Email"] = "options.tests@example.test",
                ["InternalCaller:PasswordSaltBase64"] = Convert.ToBase64String(salt),
                ["InternalCaller:PasswordHashBase64"] = Convert.ToBase64String(hash)
            };

            if (SigningKeyBase64Override is not null)
            {
                values["InternalJwt:SigningKeyBase64"] = SigningKeyBase64Override;
            }

            if (AccessTokenLifetimeOverride is not null)
            {
                values["InternalJwt:AccessTokenLifetime"] = AccessTokenLifetimeOverride;
            }

            configuration.AddInMemoryCollection(values);
        });
    }
}
