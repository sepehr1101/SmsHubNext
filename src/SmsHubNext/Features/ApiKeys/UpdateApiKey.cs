using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class UpdateApiKeyRequest
{
    public string Name { get; init; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; init; }
    public bool IsActive { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("api_keys.name_required", UserMessages.ApiKeys.NameRequired);

        return Result.Success();
    }
}
