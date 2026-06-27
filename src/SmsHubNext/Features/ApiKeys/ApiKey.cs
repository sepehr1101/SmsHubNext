namespace SmsHubNext.Features.ApiKeys;

/// <summary>
/// An API key as shown to operators (README §4.2). Never carries the secret or its
/// hash — only the non-secret <see cref="KeyPrefix"/>.
/// </summary>
public sealed record ApiKey(
    int Id,
    short CustomerId,
    string Name,
    string KeyPrefix,
    bool IsActive,
    DateTime? ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    DateTime CreatedAtUtc);
