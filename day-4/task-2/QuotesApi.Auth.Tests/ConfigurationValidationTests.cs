using QuotesApi.Configuration;

namespace QuotesApi.Auth.Tests;

public class InternalJwtOptionsTests
{
    private const string ValidIssuer = "test-issuer";
    private const string ValidAudience = "test-audience";
    private static readonly string ValidSigningKeyBase64 = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingIssuer_Throws(string? issuer)
    {
        var options = new InternalJwtOptions
        {
            Issuer = issuer,
            Audience = ValidAudience,
            SigningKeyBase64 = ValidSigningKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingAudience_Throws(string? audience)
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = audience,
            SigningKeyBase64 = ValidSigningKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingSigningKey_Throws(string? signingKeyBase64)
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = signingKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_SigningKeyNotBase64_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = "not-valid-base64!!!"
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_SigningKeyTooShort_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = Convert.ToBase64String(new byte[16])
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_NonPositiveLifetime_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = ValidSigningKeyBase64,
            AccessTokenLifetimeSeconds = 0
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }
}

public class EntraOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void ValidateAndGetAuthority_InvalidTenantId_Throws(string? tenantId)
    {
        var options = new EntraOptions
        {
            TenantId = tenantId,
            Audience = "test-audience"
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetAuthority());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetAuthority_MissingAudience_Throws(string? audience)
    {
        var options = new EntraOptions
        {
            TenantId = Guid.NewGuid().ToString(),
            Audience = audience
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetAuthority());
    }
}

public class InternalCallerOptionsTests
{
    private const string ValidUserId = "user-1";
    private const string ValidEmail = "internal.caller@example.test";
    private static readonly string ValidSaltBase64 = Convert.ToBase64String(new byte[16]);
    private static readonly string ValidHashBase64 = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Validate_MissingUserId_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = null,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingEmail_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = null,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingPasswordSalt_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = null,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingPasswordHash_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = null
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_SaltNotBase64_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = "not-valid-base64!!!",
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_HashNotBase64_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = "not-valid-base64!!!"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }
}
