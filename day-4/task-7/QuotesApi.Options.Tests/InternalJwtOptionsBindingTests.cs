using Microsoft.Extensions.Configuration;
using QuotesApi.Configuration;

namespace QuotesApi.Options.Tests;

/// <summary>
/// Pure configuration-binding tests -- no host, no DI container, just
/// IConfiguration.Get&lt;T&gt;() exactly as ASP.NET Core's options binder uses it.
/// </summary>
public class InternalJwtOptionsBindingTests
{
    private static readonly string ValidSigningKeyBase64 = Convert.ToBase64String(new byte[32]);

    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values!).Build();

    [Fact]
    public void AccessTokenLifetime_BindsFromValidTimeSpanFormat_ToExpectedNonZeroValue()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Issuer"] = "test-issuer",
            ["Audience"] = "test-audience",
            ["SigningKeyBase64"] = ValidSigningKeyBase64,
            ["AccessTokenLifetime"] = "00:15:00"
        });

        var options = config.Get<InternalJwtOptions>();

        Assert.NotNull(options);
        Assert.Equal(TimeSpan.FromMinutes(15), options!.AccessTokenLifetime);
        Assert.NotEqual(TimeSpan.Zero, options.AccessTokenLifetime);
    }

    [Fact]
    public void AccessTokenLifetime_BindsFromBareNumber_ToDaysNotZero_CorrectingTheAssumedFailureMode()
    {
        // The task text asserts a bare number "silently yields TimeSpan.Zero". Verified
        // empirically (a standalone probe against Microsoft.Extensions.Configuration.Binder)
        // that this is not what actually happens: .NET's TimeSpan config binder parses a
        // bare integer string as a day count, not seconds and not zero. "900" binds to
        // 900 DAYS -- a token that outlives any reasonable rotation policy, not one that
        // expires instantly. This test proves the real behavior rather than repeating the
        // task's unverified assumption; the practical risk is a token lifetime that's wildly
        // too long, not too short -- which is exactly why InternalJwtOptions.ValidateAndGetSigningKey()
        // rejects lifetimes above a sane maximum (see the next test), not just non-positive ones.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AccessTokenLifetime"] = "900"
        });

        var options = config.Get<InternalJwtOptions>();

        Assert.NotNull(options);
        Assert.Equal(TimeSpan.FromDays(900), options!.AccessTokenLifetime);
        Assert.NotEqual(TimeSpan.Zero, options.AccessTokenLifetime);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("15 minutes")]
    public void AccessTokenLifetime_BindsFromMalformedString_ThrowsDuringBinding(string malformed)
    {
        // Also verified, not assumed: a genuinely unparseable TimeSpan string does not bind
        // silently at all -- Microsoft.Extensions.Configuration.Binder throws immediately.
        // The pitfall is specific to bare numbers (a value that IS valid TimeSpan syntax,
        // just not the unit the author meant), not malformed strings in general.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AccessTokenLifetime"] = malformed
        });

        Assert.Throws<InvalidOperationException>(() => config.Get<InternalJwtOptions>());
    }

    [Fact]
    public void ValidateAndGetSigningKey_BareNumberMistake_ThrowsBecauseLifetimeExceedsMaximum()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Issuer"] = "test-issuer",
            ["Audience"] = "test-audience",
            ["SigningKeyBase64"] = ValidSigningKeyBase64,
            ["AccessTokenLifetime"] = "900"
        });

        var options = config.Get<InternalJwtOptions>();

        Assert.NotNull(options);
        var exception = Assert.Throws<InvalidOperationException>(
            () => options!.ValidateAndGetSigningKey());
        Assert.Contains("exceeds the maximum", exception.Message);
    }

    [Fact]
    public void ValidateAndGetSigningKey_ValidConfiguration_ReturnsExpectedKeyLength()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Issuer"] = "test-issuer",
            ["Audience"] = "test-audience",
            ["SigningKeyBase64"] = ValidSigningKeyBase64,
            ["AccessTokenLifetime"] = "00:15:00"
        });

        var options = config.Get<InternalJwtOptions>();

        Assert.NotNull(options);
        var key = options!.ValidateAndGetSigningKey();
        Assert.Equal(32, key.Length);
    }
}
