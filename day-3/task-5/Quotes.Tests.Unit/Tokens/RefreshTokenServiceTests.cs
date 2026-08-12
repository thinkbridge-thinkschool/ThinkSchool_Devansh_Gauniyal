using FluentAssertions;
using NSubstitute;
using Quotes.Tests.Unit.TestDoubles;
using Quotes.Tokens;

namespace Quotes.Tests.Unit.Tokens;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public void Issue_ValidUser_CreatesActiveRefreshToken()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1");
        var service = new RefreshTokenService(clock, tokenGenerator);

        // Act
        var result = service.Issue("user-123");

        // Assert
        result.Status.Should().Be(RefreshTokenIssueStatus.Succeeded);
        result.Token.Should().NotBeNull();
        result.Token!.Token.Should().Be("synthetic-token-1");
        result.Token.ExpiresAt.Should().Be(clock.UtcNow.Add(RefreshTokenService.RefreshTokenLifetime));
        tokenGenerator.Received(1).Generate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_UserIdIsMissing_ReturnsRejectedResult(string? userId)
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        var service = new RefreshTokenService(clock, tokenGenerator);

        // Act
        var result = service.Issue(userId);

        // Assert
        result.Status.Should().Be(RefreshTokenIssueStatus.MissingUserId);
        result.Token.Should().BeNull();
        tokenGenerator.DidNotReceive().Generate();
    }

    [Fact]
    public void Issue_TokenCreated_UsesFakeClockTime()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new FakeClock(expectedTime);
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1");
        var service = new RefreshTokenService(clock, tokenGenerator);

        // Act
        var result = service.Issue("user-123");

        // Assert
        result.Status.Should().Be(RefreshTokenIssueStatus.Succeeded);
        result.Token.Should().NotBeNull();
        result.Token!.CreatedAt.Should().Be(expectedTime);
        result.Token.ExpiresAt.Should().Be(expectedTime.Add(RefreshTokenService.RefreshTokenLifetime));
    }

    [Fact]
    public void Rotate_ActiveToken_ReturnsReplacementToken()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");

        // Act
        var result = service.Rotate(issued.Token!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.Succeeded);
        result.Replacement.Should().NotBeNull();
        result.Replacement!.Token.Should().Be("synthetic-token-2");
        result.Replacement.CreatedAt.Should().Be(clock.UtcNow);
        tokenGenerator.Received(2).Generate();
    }

    [Fact]
    public void Rotate_ActiveToken_MarksOriginalAsUsed()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");

        // Act
        var firstRotation = service.Rotate(issued.Token!.Token);
        var originalTokenAgain = service.Rotate(issued.Token.Token);

        // Assert
        firstRotation.Status.Should().Be(RefreshTokenRotationStatus.Succeeded);
        originalTokenAgain.Status.Should().Be(RefreshTokenRotationStatus.ReuseDetected);
    }

    [Fact]
    public void Rotate_UsedToken_ReturnsReuseDetected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        service.Rotate(issued.Token!.Token);

        // Act
        var result = service.Rotate(issued.Token.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.ReuseDetected);
        result.Replacement.Should().BeNull();
        tokenGenerator.Received(2).Generate();
    }

    [Fact]
    public void Rotate_UsedToken_RevokesEntireFamily()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        var rotation = service.Rotate(issued.Token!.Token);

        // Act
        var reuse = service.Rotate(issued.Token.Token);
        var descendant = service.Rotate(rotation.Replacement!.Token);

        // Assert
        reuse.Status.Should().Be(RefreshTokenRotationStatus.ReuseDetected);
        descendant.Status.Should().Be(RefreshTokenRotationStatus.FamilyRevoked);
        descendant.Replacement.Should().BeNull();
    }

    [Fact]
    public void Rotate_DescendantOfRevokedFamily_ReturnsRejected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns(
            "synthetic-token-1",
            "synthetic-token-2",
            "synthetic-token-3");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        var firstRotation = service.Rotate(issued.Token!.Token);
        var secondRotation = service.Rotate(firstRotation.Replacement!.Token);
        service.Rotate(issued.Token.Token);

        // Act
        var result = service.Rotate(secondRotation.Replacement!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.FamilyRevoked);
        result.Replacement.Should().BeNull();
        tokenGenerator.Received(3).Generate();
    }

    [Fact]
    public void Rotate_TokenHasExpired_ReturnsRejected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        clock.Advance(RefreshTokenService.RefreshTokenLifetime + TimeSpan.FromSeconds(1));

        // Act
        var result = service.Rotate(issued.Token!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.Expired);
        result.Replacement.Should().BeNull();
        tokenGenerator.Received(1).Generate();
    }

    [Fact]
    public void Rotate_TokenAtExactExpiry_ReturnsRejected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        clock.Advance(RefreshTokenService.RefreshTokenLifetime);

        // Act
        var result = service.Rotate(issued.Token!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.Expired);
        result.Replacement.Should().BeNull();
    }

    [Fact]
    public void Rotate_UnknownToken_ReturnsRejected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        var service = new RefreshTokenService(clock, tokenGenerator);

        // Act
        var result = service.Rotate("synthetic-unknown-token");

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.UnknownToken);
        result.Replacement.Should().BeNull();
        tokenGenerator.DidNotReceive().Generate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rotate_TokenIsMissing_ReturnsRejected(string? token)
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        var service = new RefreshTokenService(clock, tokenGenerator);

        // Act
        var result = service.Rotate(token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.MissingToken);
        result.Replacement.Should().BeNull();
        tokenGenerator.DidNotReceive().Generate();
    }

    [Fact]
    public void Rotate_SameTokenTwice_OnlyFirstRotationSucceeds()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");

        // Act
        var firstResult = service.Rotate(issued.Token!.Token);
        var secondResult = service.Rotate(issued.Token.Token);

        // Assert
        firstResult.Status.Should().Be(RefreshTokenRotationStatus.Succeeded);
        secondResult.Status.Should().Be(RefreshTokenRotationStatus.ReuseDetected);
        tokenGenerator.Received(2).Generate();
    }

    [Fact]
    public void Rotate_TimeAdvancedButTokenActive_ReturnsReplacementWithAdvancedTime()
    {
        // Arrange
        var initialTime = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(initialTime);
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1", "synthetic-token-2");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        clock.Advance(TimeSpan.FromDays(2));

        // Act
        var result = service.Rotate(issued.Token!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.Succeeded);
        result.Replacement.Should().NotBeNull();
        result.Replacement!.CreatedAt.Should().Be(initialTime.AddDays(2));
        result.Replacement.ExpiresAt.Should().Be(
            initialTime.AddDays(2).Add(RefreshTokenService.RefreshTokenLifetime));
    }

    [Fact]
    public void Rotate_TimeAdvancedPastExpiry_ReturnsRejected()
    {
        // Arrange
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero));
        var tokenGenerator = Substitute.For<IRefreshTokenGenerator>();
        tokenGenerator.Generate().Returns("synthetic-token-1");
        var service = new RefreshTokenService(clock, tokenGenerator);
        var issued = service.Issue("user-123");
        clock.Advance(TimeSpan.FromDays(30));

        // Act
        var result = service.Rotate(issued.Token!.Token);

        // Assert
        result.Status.Should().Be(RefreshTokenRotationStatus.Expired);
        result.Replacement.Should().BeNull();
    }
}
