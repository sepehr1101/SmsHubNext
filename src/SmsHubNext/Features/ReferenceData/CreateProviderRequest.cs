using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateProviderRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? FallbackBaseUrl { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("providers.name_required", "A provider name is required.");

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("providers.code_required", "A provider code is required.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            return Error.Validation("providers.base_url_required", "A base URL is required.");

        return Result.Success();
    }
}
