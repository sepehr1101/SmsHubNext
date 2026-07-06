namespace SmsHubNext.Features.ProviderAccounts;

public sealed record ProviderAccount(
    int Id,
    byte ProviderId,
    string ProviderCode,
    string DisplayName,
    string AuthType,
    IReadOnlyDictionary<string, string> Settings,
    bool HasSecret,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
