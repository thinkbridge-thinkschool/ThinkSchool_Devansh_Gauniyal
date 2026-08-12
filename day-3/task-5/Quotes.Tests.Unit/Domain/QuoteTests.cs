using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit.Domain;

public sealed class QuoteTests
{
    [Fact]
    public void Create_ValidInput_ReturnsQuote()
    {
        // Arrange
        const string ownerId = "user-123";
        const string text = "Small tests make changes safer.";

        // Act
        var result = Quote.Create(ownerId, text);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Value.Should().NotBeNull();
        result.Value!.OwnerId.Should().Be(ownerId);
        result.Value.Text.Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_OwnerIdIsMissing_ReturnsValidationFailure(string? ownerId)
    {
        // Arrange
        const string text = "A valid quote.";

        // Act
        var result = Quote.Create(ownerId, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be(Quote.OwnerRequiredError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_TextIsMissing_ReturnsValidationFailure(string? text)
    {
        // Arrange
        const string ownerId = "user-123";

        // Act
        var result = Quote.Create(ownerId, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be(Quote.TextRequiredError);
    }

    [Fact]
    public void Create_TextAtMaximumLength_ReturnsQuote()
    {
        // Arrange
        const string ownerId = "user-123";
        var text = new string('a', Quote.MaximumTextLength);

        // Act
        var result = Quote.Create(ownerId, text);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Text.Should().HaveLength(Quote.MaximumTextLength);
    }

    [Fact]
    public void Create_TextExceedsMaximum_ReturnsValidationFailure()
    {
        // Arrange
        const string ownerId = "user-123";
        var text = new string('a', Quote.MaximumTextLength + 1);

        // Act
        var result = Quote.Create(ownerId, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be(Quote.TextTooLongError);
    }
}
