using System.Net;

namespace QuotesApi.Telemetry.Tests;

public sealed class TelemetryIsolationTests
{
    [Fact]
    public async Task TestingEnvironment_StartsAndServesRequests_WithoutKeyVaultConfigured()
    {
        // No "KeyVault:Name" is configured anywhere in this factory. If the Testing/
        // Development guard around Azure Monitor + Key Vault resolution in Program.cs
        // were ever removed or inverted, ResolveAppInsightsConnectionString would throw
        // InvalidOperationException during host startup (missing KeyVault:Name), and
        // this request would never get a response at all -- so a real 200 here is
        // itself the proof that no Key Vault or Azure Monitor code path ran.
        using var factory = new TelemetryIsolationApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
