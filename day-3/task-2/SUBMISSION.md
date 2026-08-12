# Day 3, Task 2 — Authorization policies and claims

## 1. GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-3/task-2/day-3/task-2

## 2. Required mentor notes / deliverables

Implemented two named authorization policies in `day-3/task-2`:

- `can-edit-quotes` requires an authenticated caller with `scope=quotes.write`.
- `can-delete-own-quote` uses `OwnQuoteRequirement` and `OwnQuoteAuthorizationHandler` to compare the authenticated user's ID with the loaded quote's owner ID.

The integration tests exercise the actual API through ASP.NET Core's test server. They prove that authenticated users receive `403 Forbidden` when either policy fails, while unauthenticated callers receive `401 Unauthorized`.

## 3. Claim-based policy

Source: `EntraAuthApi/Program.cs`

```csharp
options.AddPolicy(
    AuthorizationPolicies.CanEditQuotes,
    policy => policy.RequireClaim("scope", "quotes.write"));
```

The edit endpoint applies the named policy:

```csharp
app.MapPut("/api/quotes/{id:int}", (int id, QuoteUpdateRequest request) =>
    Results.Ok(new { id, text = request.Text }))
    .RequireAuthorization(AuthorizationPolicies.CanEditQuotes);
```

## 4. Custom policy registration

Source: `EntraAuthApi/Program.cs`

```csharp
builder.Services.AddSingleton<IQuoteRepository, InMemoryQuoteRepository>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnQuoteAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.CanEditQuotes,
        policy => policy.RequireClaim("scope", "quotes.write"));

    options.AddPolicy(
        AuthorizationPolicies.CanDeleteOwnQuote,
        policy => policy.AddRequirements(new OwnQuoteRequirement()));
});
```

The delete endpoint first requires authentication, loads the quote, and then performs resource-based authorization:

```csharp
var quote = quotes.Find(id);
if (quote is null)
{
    return Results.NotFound();
}

var result = await authorization.AuthorizeAsync(
    user,
    quote,
    AuthorizationPolicies.CanDeleteOwnQuote);

return result.Succeeded
    ? Results.NoContent()
    : Results.Forbid();
```

## 5. Custom IAuthorizationRequirement

Source: `EntraAuthApi/Authorization/OwnQuoteRequirement.cs`

```csharp
public sealed class OwnQuoteRequirement : IAuthorizationRequirement;
```

## 6. Custom AuthorizationHandler

Source: `EntraAuthApi/Authorization/OwnQuoteAuthorizationHandler.cs`

```csharp
public sealed class OwnQuoteAuthorizationHandler
    : AuthorizationHandler<OwnQuoteRequirement, QuoteResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnQuoteRequirement requirement,
        QuoteResource resource)
    {
        var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrWhiteSpace(userId)
            && string.Equals(userId, resource.OwnerId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

## 7. Tests demonstrating 403

Source: `EntraAuthApi.Tests/AuthorizationPolicyTests.cs`

Claim-policy failure:

```csharp
[Fact]
public async Task EditQuote_AuthenticatedWithoutWriteScope_ReturnsForbidden()
{
    using var request = CreateEditRequest(userId: "user-1");

    var response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

Custom ownership-policy failure:

```csharp
[Fact]
public async Task DeleteQuote_AuthenticatedNonOwner_ReturnsForbidden()
{
    using var request = CreateDeleteRequest(quoteId: 1, userId: "user-2");

    var response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

## 8. Genuine test output

Command run from `day-3/task-2`:

```text
dotnet test Task2.slnx --no-build --disable-build-servers --verbosity normal
```

Result:

```text
Test Run Successful.
Total tests: 7
     Passed: 7
 Total time: 0.5398 Seconds

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Verified status matrix:

| Scenario | Actual status |
| --- | --- |
| Unauthenticated edit | `401 Unauthorized` |
| Authenticated edit without `quotes.write` | `403 Forbidden` |
| Authenticated edit with `quotes.write` | `200 OK` |
| Unauthenticated delete | `401 Unauthorized` |
| Authenticated non-owner delete | `403 Forbidden` |
| Authenticated owner delete | `204 No Content` |
| Authenticated missing quote | `404 Not Found` |

## 9. What did you learn this session?

I learned that authentication identifies a user, while authorization decides what that authenticated user may do. Named policies keep reusable claim rules and resource-dependent ownership rules separate from endpoint code.

## 10. What would break this?

Incorrect scope claim names or inconsistent user IDs would deny legitimate callers, while forgetting to apply authentication or a named policy could leave an endpoint insufficiently protected.
