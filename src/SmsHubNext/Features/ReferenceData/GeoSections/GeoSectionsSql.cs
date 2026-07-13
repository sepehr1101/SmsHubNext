namespace SmsHubNext.Features.ReferenceData.GeoSections;

internal static class GeoSectionsSql
{
    public const string List =
        "SELECT Id, ParentGeoSectionId, SectionType, Name, Code, Path, IsActive FROM dbo.GeoSection WHERE DeletedAtUtc IS NULL ORDER BY Path;";

    public const string GetPath =
        "SELECT Path FROM dbo.GeoSection WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND DeletedAtUtc IS NULL;";

    // Path is filled in after insert (it includes the new identity).
    public const string Insert =
        """
        INSERT INTO dbo.GeoSection (ParentGeoSectionId, SectionType, Name, Code, Path)
        OUTPUT INSERTED.Id
        VALUES (@ParentGeoSectionId, @SectionType, @Name, @Code, '');
        """;

    public const string UpdatePath = "UPDATE dbo.GeoSection SET Path = @Path WHERE Id = @Id;";

    public const string Update =
        """
        UPDATE dbo.GeoSection
        SET Name = @Name, Code = @Code, IsActive = @IsActive
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;

    public const string GetForDelete =
        "SELECT Id FROM dbo.GeoSection WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND DeletedAtUtc IS NULL;";

    public const string HasActiveChildren =
        """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.GeoSection
            WHERE ParentGeoSectionId = @Id AND DeletedAtUtc IS NULL
        ) THEN 1 ELSE 0 END AS bit);
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.GeoSection
        SET IsActive = 0,
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
