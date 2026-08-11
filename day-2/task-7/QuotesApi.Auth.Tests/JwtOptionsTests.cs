using FluentAssertions;
using QuotesApi.Configuration;

namespace QuotesApi.Auth.Tests;

public sealed class JwtOptionsTests
{
    [Fact]
    public void ValidateAndGetSigningKey_WhenKeyIsMissing_FailsSecurely()
    {
        var options = ValidOptions(signingKey: null);

        var act = options.ValidateAndGetSigningKey;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signing key is required*");
    }

    [Fact]
    public void ValidateAndGetSigningKey_WhenKeyIsTooShort_FailsSecurely()
    {
        var options = ValidOptions(Convert.ToBase64String(new byte[31]));

        var act = options.ValidateAndGetSigningKey;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void ValidateAndGetSigningKey_WhenLifetimeIsInvalid_FailsSecurely()
    {
        var options = new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
            AccessTokenLifetimeSeconds = 0
        };

        var act = options.ValidateAndGetSigningKey;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lifetime*");
    }

    private static JwtOptions ValidOptions(string? signingKey) => new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKeyBase64 = signingKey,
        AccessTokenLifetimeSeconds = 900
    };
}
