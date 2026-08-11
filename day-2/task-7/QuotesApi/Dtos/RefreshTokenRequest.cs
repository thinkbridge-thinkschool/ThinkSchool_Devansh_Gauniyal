using System.Text.Json.Serialization;

namespace QuotesApi.Dtos;

public sealed record RefreshTokenRequest(
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);
