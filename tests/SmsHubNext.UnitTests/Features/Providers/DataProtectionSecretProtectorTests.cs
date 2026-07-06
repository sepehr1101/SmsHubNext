using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SmsHubNext.Features.Providers;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Providers;

public sealed class DataProtectionSecretProtectorTests
{
    [Fact]
    public void Protect_round_trips_provider_secret()
    {
        ISecretProtector protector = NewProtector();

        byte[] cipher = protector.Protect("secret-password");
        string restored = protector.Unprotect(cipher);

        Assert.Equal("secret-password", restored);
    }

    [Fact]
    public void Protected_payload_does_not_store_plaintext_secret()
    {
        ISecretProtector protector = NewProtector();

        byte[] cipher = protector.Protect("secret-password");
        string cipherText = Encoding.UTF8.GetString(cipher);

        Assert.DoesNotContain("secret-password", cipherText);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Protect_rejects_blank_secret(string secret)
    {
        ISecretProtector protector = NewProtector();

        Assert.Throws<ArgumentException>(() => protector.Protect(secret));
    }

    [Fact]
    public void Unprotect_rejects_empty_cipher()
    {
        ISecretProtector protector = NewProtector();

        Assert.Throws<ArgumentException>(() => protector.Unprotect([]));
    }

    private static ISecretProtector NewProtector()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        return new DataProtectionSecretProtector(provider);
    }
}
