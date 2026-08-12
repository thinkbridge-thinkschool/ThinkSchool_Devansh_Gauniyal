namespace QuotesApi.Configuration;

public sealed class EntraOptions
{
    public const string SectionName = "Entra";

    public string? TenantId { get; init; }
    public string? Audience { get; init; }

    public string ValidateAndGetAuthority()
    {
        if (!Guid.TryParse(TenantId, out _))
        {
            throw new InvalidOperationException(
                "Entra tenant ID must be a valid GUID.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Entra audience is required.");
        }

        return $"https://login.microsoftonline.com/{TenantId}/v2.0";
    }
}
