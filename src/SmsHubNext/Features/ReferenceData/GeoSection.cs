using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.ReferenceData;

/// <summary>A node in the geographic hierarchy (README §4.7).</summary>
public sealed record GeoSection(
    int Id,
    int? ParentGeoSectionId,
    GeoSectionType SectionType,
    string Name,
    string Code,
    string Path,
    bool IsActive);
