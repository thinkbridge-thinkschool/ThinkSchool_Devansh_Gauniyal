using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Telemetry.Tests;

/// <summary>
/// Deliberately does NOT configure "KeyVault:Name" anywhere. If Program.cs's Testing/
/// Development guard around the Azure Monitor + Key Vault code path were ever removed
/// or inverted, ResolveAppInsightsConnectionString would throw at startup because that
/// key is missing here -- so a successful request through this factory is itself proof
/// the Azure/Key Vault path was never reached.
/// </summary>
public sealed class TelemetryIsolationApiFactory : WebApplicationFactory<Program>
{
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

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "quotes-api.telemetry-tests",
                ["InternalJwt:Audience"] = "quotes-api.telemetry-test-clients",
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["InternalJwt:AccessTokenLifetime"] = "00:15:00",
                ["Entra:TenantId"] = Guid.NewGuid().ToString(),
                ["Entra:Audience"] = "quotes-api.telemetry-tests-entra-audience",
                ["InternalCaller:UserId"] = "telemetry-test-user",
                ["InternalCaller:Email"] = "telemetry.tests@example.test",
                ["InternalCaller:PasswordSaltBase64"] = Convert.ToBase64String(salt),
                ["InternalCaller:PasswordHashBase64"] = Convert.ToBase64String(hash)
                // Deliberately no "KeyVault:Name" entry.
            });
        });
    }
}
