using System.Security.Cryptography;

namespace Quotes.Tokens;

public sealed class CryptoRefreshTokenGenerator : IRefreshTokenGenerator
{
    public string Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return token
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
