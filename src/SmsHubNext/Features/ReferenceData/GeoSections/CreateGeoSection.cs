using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

public sealed class CreateGeoSectionRequest
{
    public int? ParentGeoSectionId { get; init; }
    public GeoSectionType SectionType { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    public Result Validate()
    {
        if (!Enum.IsDefined(SectionType))
            return Error.Validation("geo.section_type_invalid", UserMessages.ReferenceData.GeoSectionTypeInvalid);

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("geo.name_required", UserMessages.ReferenceData.NameRequired);

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("geo.code_required", UserMessages.ReferenceData.CodeRequired);

        return Result.Success();
    }
}

public sealed record CreateGeoSectionResponse(int Id, string Path);
