namespace QuotesApi.Authorization;

public static class AuthorizationPolicies
{
    public const string CanEditQuotes = "can-edit-quotes";
    public const string CanDeleteOwnQuote = "can-delete-own-quote";
}
