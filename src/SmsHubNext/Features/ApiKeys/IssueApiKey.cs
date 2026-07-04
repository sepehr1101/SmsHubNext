using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class IssueApiKeyRequest
{
    public short CustomerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; init; }

    public Result Validate()
    {
        if (CustomerId <= 0)
            return Error.Validation("api_keys.customer_required", UserMessages.ApiKeys.CustomerRequired);

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("api_keys.name_required", UserMessages.ApiKeys.NameRequired);

        return Result.Success();
    }
}

/// <summary>
/// A newly issued key. <see cref="Key"/> is the plaintext secret — returned
/// exactly once at creation and never stored or shown again.
/// </summary>
public sealed record IssueApiKeyResponse(int Id, string KeyPrefix, string Key);
