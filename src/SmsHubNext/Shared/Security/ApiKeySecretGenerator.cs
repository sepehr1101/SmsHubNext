using System.Security.Cryptography;

namespace SmsHubNext.Shared.Security;

public static class ApiKeySecretGenerator
{
    private const string KeyLabel = "shn_";

    public static GeneratedApiKey Generate()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(raw)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        string secret = KeyLabel + token;

        return new GeneratedApiKey(
            secret,
            secret[..12],
            ApiKeyHasher.HashBytes(secret));
    }
}

public sealed record GeneratedApiKey(string Secret, string Prefix, byte[] Hash);
