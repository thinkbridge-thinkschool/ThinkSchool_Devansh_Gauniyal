using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace EntraAuthApi.Authorization;

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
