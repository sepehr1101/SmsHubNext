using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

public sealed class CreateProviderRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? FallbackBaseUrl { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("providers.name_required", UserMessages.ReferenceData.ProviderNameRequired);

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("providers.code_required", UserMessages.ReferenceData.ProviderCodeRequired);

        if (string.IsNullOrWhiteSpace(BaseUrl))
            return Error.Validation("providers.base_url_required", UserMessages.ReferenceData.BaseUrlRequired);

        return Result.Success();
    }
}

public sealed record CreateProviderResponse(byte Id);
