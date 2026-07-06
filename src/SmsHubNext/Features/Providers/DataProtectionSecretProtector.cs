using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// Protects provider account secrets before they are stored in SQL Server.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "SmsHubNext.ProviderAccount.Secret.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public byte[] Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(secret, JsonOptions);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
            throw new ArgumentException("Secret cipher must not be empty.", nameof(cipher));

        byte[] plaintext = _protector.Unprotect(cipher);
        string? secret = JsonSerializer.Deserialize<string>(plaintext, JsonOptions);

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Could not deserialize provider secret.");

        return secret;
    }
}
