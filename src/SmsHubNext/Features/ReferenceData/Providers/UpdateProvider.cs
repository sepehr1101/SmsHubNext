using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

public sealed class UpdateProviderRequest
{
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? FallbackBaseUrl { get; init; }
    public bool IsActive { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("providers.name_required", UserMessages.ReferenceData.ProviderNameRequired);

        if (string.IsNullOrWhiteSpace(BaseUrl))
            return Error.Validation("providers.base_url_required", UserMessages.ReferenceData.BaseUrlRequired);

        return Result.Success();
    }
}
