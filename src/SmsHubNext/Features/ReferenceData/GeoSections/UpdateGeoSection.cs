using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

public sealed class UpdateGeoSectionRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("geo.name_required", UserMessages.ReferenceData.NameRequired);

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("geo.code_required", UserMessages.ReferenceData.CodeRequired);

        return Result.Success();
    }
}
