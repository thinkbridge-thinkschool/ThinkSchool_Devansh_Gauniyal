using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuthApi.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public string InternalIssuer { get; } = "EntraAuthApi.Tests.Internal";
    public string InternalAudience { get; } = "EntraAuthApi.Tests.InternalClients";
    public byte[] InternalSigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    public string TenantId { get; } = "11111111-1111-1111-1111-111111111111";
    public string ClientId { get; } = "22222222-2222-2222-2222-222222222222";
    public string EntraAudience => ClientId;
    public byte[] EntraSigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    public string EntraIssuer =>
        $"https://login.microsoftonline.com/{TenantId}/v2.0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = InternalIssuer,
                ["InternalJwt:Audience"] = InternalAudience,
                ["InternalJwt:SigningKeyBase64"] = Convert.ToBase64String(InternalSigningKey),
                ["Entra:TenantId"] = TenantId,
                ["Entra:ClientId"] = ClientId,
                ["Entra:Audience"] = EntraAudience
            });
        });
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(
                AuthenticationSchemes.EntraId,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = EntraIssuer,
                        SigningKeys =
                        {
                            new SymmetricSecurityKey(EntraSigningKey)
                        }
                    };
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            configuration);
                });
        });
    }
}
