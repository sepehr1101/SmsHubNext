using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateGeoSectionRequest
{
    public int? ParentGeoSectionId { get; init; }
    public GeoSectionType SectionType { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    public Result Validate()
    {
        if (!Enum.IsDefined(SectionType))
            return Error.Validation("geo.section_type_invalid", "A valid section type is required.");

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("geo.name_required", "A name is required.");

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("geo.code_required", "A code is required.");

        return Result.Success();
    }
}

public sealed record CreateGeoSectionResponse(int Id, string Path);
