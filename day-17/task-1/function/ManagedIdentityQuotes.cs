// This HTTP function acquires a token with the Function App's system-assigned managed
// identity, proves that token against the authorized GET /api/protected endpoint, and
// only then reads GET /api/quotes for the Angular UI. It deliberately proves access
// with /api/protected because the quotes list is anonymous and cannot demonstrate auth.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ManagedIdentityBridge;

public sealed class ManagedIdentityQuotes(
    TokenCredential credential,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ManagedIdentityQuotes> logger)
{
    private const string RequiredRole = "Api.Access";

    [Function("ManagedIdentityQuotes")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "quotes")]
        HttpRequestData request,
        FunctionContext context)
    {
        var apiBaseUrl = configuration["QuotesApiBaseUrl"]?.TrimEnd('/');
        var audience = configuration["EntraAudience"]?.TrimEnd('/');

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || apiBaseUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(audience))
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.InternalServerError,
                "bridge_configuration_invalid",
                "The Function App requires an HTTPS QuotesApiBaseUrl and an EntraAudience setting.");
        }

        try
        {
            var accessToken = await credential.GetTokenAsync(
                new TokenRequestContext([$"{audience}/.default"]),
                context.CancellationToken);

            var claims = ReadClaims(accessToken.Token);
            var audienceVerified = claims.Audiences.Contains(audience, StringComparer.Ordinal);
            var roleVerified = claims.Roles.Contains(RequiredRole, StringComparer.Ordinal);

            logger.LogInformation(
                "Managed identity token checked without logging its value. Audience matched: {AudienceVerified}; issuer present: {IssuerPresent}; required role present: {RoleVerified}.",
                audienceVerified,
                !string.IsNullOrWhiteSpace(claims.Issuer),
                roleVerified);

            if (!audienceVerified || !roleVerified)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.Forbidden,
                    "managed_identity_claims_invalid",
                    "The managed-identity token did not contain the configured audience and Api.Access role.");
            }

            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken.Token);

            using var protectedResponse = await client.GetAsync(
                new Uri(apiBaseUri, "/api/protected"),
                context.CancellationToken);

            if (!protectedResponse.IsSuccessStatusCode)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.BadGateway,
                    "authorized_endpoint_rejected_token",
                    $"GET /api/protected returned {(int)protectedResponse.StatusCode}.");
            }

            using var quotesResponse = await client.GetAsync(
                new Uri(apiBaseUri, "/api/quotes"),
                context.CancellationToken);

            if (!quotesResponse.IsSuccessStatusCode)
            {
                return await ErrorAsync(
                    request,
                    HttpStatusCode.BadGateway,
                    "quotes_endpoint_failed",
                    $"GET /api/quotes returned {(int)quotesResponse.StatusCode}.");
            }

            var response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.Headers.Add("X-Managed-Identity-Audience-Verified", "true");
            response.Headers.Add("X-Managed-Identity-Role-Verified", RequiredRole);
            response.Headers.Add("X-Managed-Identity-Authorized-Endpoint", "/api/protected");
            await response.WriteStringAsync(
                await quotesResponse.Content.ReadAsStringAsync(context.CancellationToken),
                context.CancellationToken);
            return response;
        }
        catch (CredentialUnavailableException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_unavailable",
                "The Function App could not access its system-assigned managed identity.");
        }
        catch (AuthenticationFailedException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_authentication_failed",
                "Azure rejected the Function App's managed-identity token request.");
        }
        catch (HttpRequestException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "quotes_api_unreachable",
                "The Function App could not reach the Quotes API over HTTPS.");
        }
        catch (InvalidOperationException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "managed_identity_token_invalid",
                "The managed-identity token could not be validated locally.");
        }
    }

    private static TokenClaims ReadClaims(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("The access token is not a JWT.");
        }

        var payload = segments[1]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = document.RootElement;
        var audiences = ReadStringOrArray(root, "aud");
        var roles = ReadStringOrArray(root, "roles");
        var issuer = root.TryGetProperty("iss", out var issuerElement)
            ? issuerElement.GetString()
            : null;

        return new TokenClaims(audiences, roles, issuer);
    }

    private static IReadOnlyList<string> ReadStringOrArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() is { } value ? [value] : [];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .OfType<string>()
                .ToArray();
        }

        return [];
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request,
        HttpStatusCode status,
        string code,
        string message)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(new { error = new { code, message } }),
            Encoding.UTF8);
        return response;
    }

    private sealed record TokenClaims(
        IReadOnlyList<string> Audiences,
        IReadOnlyList<string> Roles,
        string? Issuer);
}
