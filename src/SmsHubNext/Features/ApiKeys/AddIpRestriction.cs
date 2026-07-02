using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

public sealed class AddIpRestrictionRequest
{
    public string Cidr { get; init; } = string.Empty;
    public string? Description { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Cidr))
            return Error.Validation("api_keys.cidr_required", "A CIDR range is required.");

        return Result.Success();
    }
}
