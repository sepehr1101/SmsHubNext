namespace SmsHubNext.Features.ApiKeys;

/// <summary>
/// A newly issued key. <see cref="Key"/> is the plaintext secret — returned
/// exactly once at creation and never stored or shown again.
/// </summary>
public sealed record IssueApiKeyResponse(int Id, string KeyPrefix, string Key);
