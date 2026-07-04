namespace SmsHubNext.Features.Providers;

/// <summary>
/// Provider credential material before encryption. The database only stores the protected binary blob.
/// </summary>
public sealed record ProviderCredentialPayload(
    string ProviderCode,
    string AccountKey,
    IReadOnlyDictionary<string, string> Secrets)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountKey);

        if (Secrets.Count == 0)
            throw new ArgumentException("At least one secret value is required.", nameof(Secrets));

        foreach ((string key, string value) in Secrets)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        }
    }
}
