using System.Reflection;
using FluentAssertions;
using QuotesApi.Models;
using Xunit;

namespace Tests.Domain.Quotes;

public sealed class QuoteTests
{
    [Fact]
    public void Create_WithOneCharacterAuthorAndText_Succeeds()
    {
        var result = Quote.Create(author: "A", text: "T");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Author.Should().Be("A");
        result.Value.Text.Should().Be("T");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingText_ReturnsDomainError(string? text)
    {
        var result = Quote.Create(author: "Author", text: text);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("quote.text.required");
    }

    [Fact]
    public void Create_WithTextLongerThan1000Characters_ReturnsDomainError()
    {
        var result = Quote.Create(author: "Author", text: new string('t', 1001));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("quote.text.too_long");
    }

    [Fact]
    public void Create_WithExactly1000TextCharacters_Succeeds()
    {
        var result = Quote.Create(author: "Author", text: new string('t', 1000));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Text.Should().HaveLength(1000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingAuthor_ReturnsDomainError(string? author)
    {
        var result = Quote.Create(author, text: "Text");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("quote.author.required");
    }

    [Fact]
    public void Create_WithAuthorLongerThan200Characters_ReturnsDomainError()
    {
        var result = Quote.Create(author: new string('a', 201), text: "Text");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("quote.author.too_long");
    }

    [Fact]
    public void Create_WithExactly200AuthorCharacters_Succeeds()
    {
        var result = Quote.Create(author: new string('a', 200), text: "Text");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Author.Should().HaveLength(200);
    }

    [Fact]
    public void SoftDelete_MarksQuoteDeleted()
    {
        var quote = Quote.Create(author: "Author", text: "Text").Value!;

        quote.SoftDelete();

        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Text_HasNoPublicSetterOrMutationMethod()
    {
        var textProperty = typeof(Quote).GetProperty(nameof(Quote.Text));
        var textMutationMethod = typeof(Quote).GetMethod(
            "UpdateText",
            BindingFlags.Instance | BindingFlags.Public);

        textProperty!.SetMethod!.IsPublic.Should().BeFalse();
        textMutationMethod.Should().BeNull();
    }
}
