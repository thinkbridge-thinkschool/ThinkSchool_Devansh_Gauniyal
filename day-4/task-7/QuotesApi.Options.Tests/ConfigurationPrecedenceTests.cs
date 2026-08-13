using Microsoft.Extensions.Configuration;
using QuotesApi.Configuration;

namespace QuotesApi.Options.Tests;

/// <summary>
/// Proves the precedence chain the task opens with (environment variables beat
/// appsettings.json) using the exact mechanism .NET's configuration system relies
/// on for it: whichever provider is added LAST wins for a given key. This is not a
/// simulation of that mechanism -- it's the real one. WebApplication.CreateBuilder
/// gets the same result by adding appsettings.json first and
/// AddEnvironmentVariables() afterward; this test proves the underlying rule
/// directly rather than asserting the framework does the right thing internally.
/// </summary>
public class ConfigurationPrecedenceTests
{
    [Fact]
    public void ProviderAddedLast_WinsOverProviderAddedFirst_ForTheSameKey()
    {
        var appsettingsLike = new Dictionary<string, string?>
        {
            ["InternalJwt:Audience"] = "from-appsettings-json"
        };
        var environmentVariableLike = new Dictionary<string, string?>
        {
            ["InternalJwt:Audience"] = "from-environment-variable"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(appsettingsLike) // stands in for appsettings.json
            .AddInMemoryCollection(environmentVariableLike) // stands in for env vars, added after
            .Build();

        Assert.Equal("from-environment-variable", config["InternalJwt:Audience"]);
    }

    [Fact]
    public void EnvironmentVariable_OverridesAppsettingsValue_WhenBoundIntoInternalJwtOptions()
    {
        var appsettingsLike = new Dictionary<string, string?>
        {
            ["Issuer"] = "appsettings-issuer",
            ["Audience"] = "appsettings-audience",
            ["AccessTokenLifetime"] = "00:15:00"
        };
        var environmentVariableLike = new Dictionary<string, string?>
        {
            ["Audience"] = "environment-audience" // only this key is "overridden"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(appsettingsLike)
            .AddInMemoryCollection(environmentVariableLike)
            .Build();

        var options = config.Get<InternalJwtOptions>();

        Assert.NotNull(options);
        Assert.Equal("environment-audience", options!.Audience); // overridden key: env wins
        Assert.Equal("appsettings-issuer", options.Issuer); // untouched key: appsettings value stands
    }
}
