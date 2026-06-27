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
            return Error.Validation("api_keys.customer_required", "A valid customer id is required.");

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("api_keys.name_required", "A key name is required.");

        return Result.Success();
    }
}
