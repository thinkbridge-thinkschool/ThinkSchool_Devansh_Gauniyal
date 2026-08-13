using System.Net;

namespace QuotesApi.Options.Tests;

/// <summary>
/// Proves ValidateOnStart() genuinely fails host startup -- not just a later call to
/// IOptions&lt;T&gt;.Value -- exercising the real Program.cs wiring end to end via
/// WebApplicationFactory, not a reimplementation of the AddOptions/Bind/Validate chain.
/// </summary>
public sealed class InternalJwtOptionsStartupValidationTests
{
    [Fact]
    public void MissingSigningKey_HostFailsToStart()
    {
        using var factory = new OptionsValidationApiFactory { SigningKeyBase64Override = null };

        // WebApplicationFactory builds and starts the real host on first server access.
        // ValidateOnStart's hosted service runs during that startup, so the missing
        // signing key must surface here, not silently later.
        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(
            "Internal JWT signing key is required",
            FindMessageInChain(exception));
    }

    [Fact]
    public void LifetimeFromBareNumberMistake_HostFailsToStart()
    {
        using var factory = new OptionsValidationApiFactory { AccessTokenLifetimeOverride = "900" };

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("exceeds the maximum", FindMessageInChain(exception));
    }

    [Fact]
    public async Task ValidConfiguration_HostStartsSuccessfully_AndServesRequests()
    {
        using var factory = new OptionsValidationApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string FindMessageInChain(Exception exception)
    {
        var current = (Exception?)exception;
        while (current is not null)
        {
            if (current.Message.Contains("Internal JWT")
                || current.Message.Contains("exceeds the maximum"))
            {
                return current.Message;
            }

            current = current.InnerException;
        }

        return exception.ToString();
    }
}
