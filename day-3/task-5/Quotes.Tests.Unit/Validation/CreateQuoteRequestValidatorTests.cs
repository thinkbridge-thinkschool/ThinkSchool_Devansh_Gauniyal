using FluentAssertions;
using Quotes.Domain;
using Quotes.Validation;

namespace Quotes.Tests.Unit.Validation;

public sealed class CreateQuoteRequestValidatorTests
{
    [Fact]
    public void Validate_RequestIsNull_ReturnsInvalidResult()
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();

        // Act
        var result = validator.Validate(null);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(CreateQuoteRequestValidator.RequestRequiredError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_OwnerIdIsMissing_ReturnsInvalidResult(string? ownerId)
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(ownerId, "A valid quote.");

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(Quote.OwnerRequiredError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TextIsMissing_ReturnsInvalidResult(string? text)
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("user-123", text);

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(Quote.TextRequiredError);
    }

    [Fact]
    public void Validate_TextAtMaximumLength_ReturnsValidResult()
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(
            "user-123",
            new string('a', Quote.MaximumTextLength));

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_TextExceedsMaximum_ReturnsInvalidResult()
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(
            "user-123",
            new string('a', Quote.MaximumTextLength + 1));

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(Quote.TextTooLongError);
    }

    [Fact]
    public void Validate_OwnerAndTextAreMissing_ReturnsBothErrors()
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(null, null);

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(
            Quote.OwnerRequiredError,
            Quote.TextRequiredError);
    }

    [Fact]
    public void Validate_NormalValidInput_ReturnsValidResult()
    {
        // Arrange
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("user-123", "A valid quote.");

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
