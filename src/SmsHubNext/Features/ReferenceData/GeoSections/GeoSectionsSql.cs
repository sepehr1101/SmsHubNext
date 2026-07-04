namespace SmsHubNext.Features.ReferenceData.GeoSections;

internal static class GeoSectionsSql
{
    public const string List =
        "SELECT Id, ParentGeoSectionId, SectionType, Name, Code, Path, IsActive FROM dbo.GeoSection ORDER BY Path;";

    public const string GetPath = "SELECT Path FROM dbo.GeoSection WHERE Id = @Id;";

    // Path is filled in after insert (it includes the new identity).
    public const string Insert =
        """
        INSERT INTO dbo.GeoSection (ParentGeoSectionId, SectionType, Name, Code, Path)
        OUTPUT INSERTED.Id
        VALUES (@ParentGeoSectionId, @SectionType, @Name, @Code, '');
        """;

    public const string UpdatePath = "UPDATE dbo.GeoSection SET Path = @Path WHERE Id = @Id;";
}
