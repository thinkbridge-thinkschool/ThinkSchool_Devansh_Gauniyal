using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntraAuthApi.Tests;

public sealed class AuthorizationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "EntraAuthApi.Tests.Internal",
                ["InternalJwt:Audience"] = "EntraAuthApi.Tests.InternalClients",
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Entra:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["Entra:ClientId"] = "22222222-2222-2222-2222-222222222222",
                ["Entra:Audience"] = "22222222-2222-2222-2222-222222222222"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}
