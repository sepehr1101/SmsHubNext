using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SmsHubNext.Features.Providers;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Providers;

public sealed class ProviderCredentialProtectorTests
{
    [Fact]
    public void Protect_round_trips_provider_credentials()
    {
        ProviderCredentialProtector protector = NewProtector();
        ProviderCredentialPayload payload = Payload();

        byte[] cipher = protector.Protect(payload);
        ProviderCredentialPayload restored = protector.Unprotect(cipher);

        Assert.Equal(payload.ProviderCode, restored.ProviderCode);
        Assert.Equal(payload.AccountKey, restored.AccountKey);
        Assert.Equal(payload.Secrets, restored.Secrets);
    }

    [Fact]
    public void Protected_payload_does_not_store_plaintext_json()
    {
        ProviderCredentialProtector protector = NewProtector();
        ProviderCredentialPayload payload = Payload();

        byte[] cipher = protector.Protect(payload);
        string cipherText = Encoding.UTF8.GetString(cipher);

        Assert.DoesNotContain("secret-password", cipherText);
        Assert.DoesNotContain("apiKey", cipherText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "main", "apiKey", "secret-password")]
    [InlineData("kavenegar", "", "apiKey", "secret-password")]
    [InlineData("kavenegar", "main", "", "secret-password")]
    [InlineData("kavenegar", "main", "apiKey", "")]
    public void Protect_rejects_blank_payload_fields(
        string providerCode,
        string accountKey,
        string secretKey,
        string secretValue)
    {
        ProviderCredentialProtector protector = NewProtector();
        ProviderCredentialPayload payload = new(
            providerCode,
            accountKey,
            new Dictionary<string, string> { [secretKey] = secretValue });

        Assert.Throws<ArgumentException>(() => protector.Protect(payload));
    }

    [Fact]
    public void Unprotect_rejects_empty_cipher()
    {
        ProviderCredentialProtector protector = NewProtector();

        Assert.Throws<ArgumentException>(() => protector.Unprotect([]));
    }

    private static ProviderCredentialProtector NewProtector()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        return new ProviderCredentialProtector(provider);
    }

    private static ProviderCredentialPayload Payload() => new(
        "kavenegar",
        "main",
        new Dictionary<string, string>
        {
            ["apiKey"] = "secret-password",
        });
}
