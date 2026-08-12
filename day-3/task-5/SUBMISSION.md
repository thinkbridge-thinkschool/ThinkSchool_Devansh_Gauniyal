# Day 3 — Task 5: xUnit with Fluent Assertions

## 1. GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-3/task-5/day-3/task-5

## 2. Required mentor notes/deliverables

The suite contains 44 genuine unit-test cases. All 44 passed, with 0 failed and 0 skipped.

### 3. Sample test 1 — Quote.Create

```csharp
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
```

### 4. Sample test 2 — Validator theory

```csharp
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
```

### 5. Sample test 3 — Refresh-token reuse with FakeClock and NSubstitute

```csharp
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
```

### 6. Genuine test command

Working directory: `/Users/devansh/thinkschool/day-3/task-5`

```text
dotnet test Task5.slnx --no-build --verbosity normal
```

### 7. Genuine test output

```text
Test Run Successful.
Total tests: 44
     Passed: 44
 Total time: 0.3794 Seconds

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.74
```

### 8. Total test time

0.3794 seconds, as reported by the test runner.

## 9. What did you learn this session?

I learned how to structure fast unit tests using Arrange–Act–Assert, xUnit, and FluentAssertions. Injecting `IClock` and a small token-generator dependency makes time-dependent refresh-token behavior deterministic without real delays.

## 10. What would break this?

Tests could become unreliable if production services read the real system clock or share mutable refresh-token state. New validation branches could also be missed if they are added without matching unit tests.
