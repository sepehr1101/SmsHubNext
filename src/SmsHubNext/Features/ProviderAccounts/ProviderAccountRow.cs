namespace SmsHubNext.Features.ProviderAccounts;

internal sealed record ProviderAccountRow(
    int Id,
    byte ProviderId,
    string ProviderCode,
    string DisplayName,
    string AuthType,
    string SettingsJson,
    bool HasSecret,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public ProviderAccount ToModel() => new(
        Id,
        ProviderId,
        ProviderCode,
        DisplayName,
        AuthType,
        ProviderAccountSettings.FromJson(SettingsJson),
        HasSecret,
        IsActive,
        CreatedAtUtc,
        UpdatedAtUtc);
}
