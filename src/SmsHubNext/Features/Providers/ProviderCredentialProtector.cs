using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// Protects provider credentials before they are stored in SQL Server.
/// </summary>
public sealed class ProviderCredentialProtector
{
    private const string Purpose = "SmsHubNext.ProviderCredential.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;

    public ProviderCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public byte[] Protect(ProviderCredentialPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        payload.Validate();

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return _protector.Protect(plaintext);
    }

    public ProviderCredentialPayload Unprotect(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
            throw new ArgumentException("Credential cipher must not be empty.", nameof(cipher));

        byte[] plaintext = _protector.Unprotect(cipher);
        ProviderCredentialPayload? payload =
            JsonSerializer.Deserialize<ProviderCredentialPayload>(plaintext, JsonOptions);

        if (payload is null)
            throw new InvalidOperationException("Could not deserialize provider credential payload.");

        payload.Validate();
        return payload;
    }
}
