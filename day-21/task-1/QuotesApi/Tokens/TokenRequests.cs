using System.Text.Json.Serialization;

namespace QuotesApi.Tokens;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);

public sealed record TokenPair(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
