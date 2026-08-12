using FluentAssertions;
using Quotes.Validation;

namespace Quotes.Tests.Unit.Validation;

public sealed class RefreshTokenRequestValidatorTests
{
    [Fact]
    public void Validate_RequestIsNull_ReturnsInvalidResult()
    {
        // Arrange
        var validator = new RefreshTokenRequestValidator();

        // Act
        var result = validator.Validate(null);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(RefreshTokenRequestValidator.RequestRequiredError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TokenIsMissing_ReturnsInvalidResult(string? token)
    {
        // Arrange
        var validator = new RefreshTokenRequestValidator();
        var request = new RefreshTokenRequest(token);

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(RefreshTokenRequestValidator.TokenRequiredError);
    }

    [Fact]
    public void Validate_TokenIsPresent_ReturnsValidResult()
    {
        // Arrange
        var validator = new RefreshTokenRequestValidator();
        var request = new RefreshTokenRequest("synthetic-refresh-token");

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
