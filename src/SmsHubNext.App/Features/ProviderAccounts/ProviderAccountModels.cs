namespace SmsHubNext.App.Features.ProviderAccounts;

public sealed record ProviderAccountItem(
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

public sealed record CreateProviderAccountRequest(
    string ProviderCode,
    string DisplayName,
    string AuthType,
    IReadOnlyDictionary<string, string> Settings,
    string Secret,
    bool IsActive);

public sealed record CreateProviderAccountResponse(int Id);

public sealed record UpdateProviderAccountRequest(
    string ProviderCode,
    string DisplayName,
    string AuthType,
    IReadOnlyDictionary<string, string> Settings,
    string? Secret,
    bool IsActive);
