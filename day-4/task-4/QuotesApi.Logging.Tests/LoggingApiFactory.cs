using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace QuotesApi.Logging.Tests;

public sealed class LoggingApiFactory : WebApplicationFactory<Program>
{
    public string Email { get; } = "logging.tests.caller@example.test";
    public string Password { get; } = "test-password-not-real-" + Guid.NewGuid().ToString("N");

    public InMemorySink Sink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(Password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "quotes-api.logging-tests",
                ["InternalJwt:Audience"] = "quotes-api.logging-test-clients",
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["InternalJwt:AccessTokenLifetime"] = "00:15:00",
                ["Entra:TenantId"] = Guid.NewGuid().ToString(),
                ["Entra:Audience"] = "quotes-api.logging-tests-entra-audience",
                ["InternalCaller:UserId"] = "logging-test-user",
                ["InternalCaller:Email"] = Email,
                ["InternalCaller:PasswordSaltBase64"] = Convert.ToBase64String(salt),
                ["InternalCaller:PasswordHashBase64"] = Convert.ToBase64String(hash)
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Sink);
        });
    }
}
