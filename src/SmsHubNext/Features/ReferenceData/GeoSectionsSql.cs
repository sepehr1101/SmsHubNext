namespace SmsHubNext.Features.ReferenceData;

internal static class GeoSectionsSql
{
    public const string List =
        "SELECT Id, ParentGeoSectionId, SectionType, Name, Code, Path, IsActive FROM dbo.GeoSection ORDER BY Path;";
}
