namespace SmsHubNext.Features.ProviderAccounts;

internal static class ProviderAccountsSql
{
    public const string List =
        """
        SELECT
            pa.Id,
            pa.ProviderId,
            p.Code AS ProviderCode,
            pa.DisplayName,
            pa.AuthType,
            pa.SettingsJson,
            CAST(CASE WHEN pa.SecretEncrypted IS NULL THEN 0 ELSE 1 END AS bit) AS HasSecret,
            pa.IsActive,
            pa.CreatedAtUtc,
            pa.UpdatedAtUtc
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        WHERE p.DeletedAtUtc IS NULL AND pa.DeletedAtUtc IS NULL
        ORDER BY pa.Id;
        """;

    public const string Get =
        """
        SELECT
            pa.Id,
            pa.ProviderId,
            p.Code AS ProviderCode,
            pa.DisplayName,
            pa.AuthType,
            pa.SettingsJson,
            CAST(CASE WHEN pa.SecretEncrypted IS NULL THEN 0 ELSE 1 END AS bit) AS HasSecret,
            pa.IsActive,
            pa.CreatedAtUtc,
            pa.UpdatedAtUtc
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Id = pa.ProviderId
        WHERE pa.Id = @Id AND p.DeletedAtUtc IS NULL AND pa.DeletedAtUtc IS NULL;
        """;

    public const string Insert =
        """
        INSERT INTO dbo.ProviderAccount
            (ProviderId, DisplayName, AuthType, SettingsJson, SecretEncrypted, IsActive)
        OUTPUT INSERTED.Id
        SELECT p.Id, @DisplayName, @AuthType, @SettingsJson, @SecretEncrypted, @IsActive
        FROM dbo.Provider p
        WHERE p.Code = @ProviderCode AND p.DeletedAtUtc IS NULL;
        """;

    public const string UpdateWithSecret =
        """
        UPDATE pa
        SET
            ProviderId = p.Id,
            DisplayName = @DisplayName,
            AuthType = @AuthType,
            SettingsJson = @SettingsJson,
            SecretEncrypted = @SecretEncrypted,
            IsActive = @IsActive,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Code = @ProviderCode
        WHERE pa.Id = @Id AND p.DeletedAtUtc IS NULL AND pa.DeletedAtUtc IS NULL;
        """;

    public const string UpdateWithoutSecret =
        """
        UPDATE pa
        SET
            ProviderId = p.Id,
            DisplayName = @DisplayName,
            AuthType = @AuthType,
            SettingsJson = @SettingsJson,
            IsActive = @IsActive,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.ProviderAccount pa
        INNER JOIN dbo.Provider p ON p.Code = @ProviderCode
        WHERE pa.Id = @Id AND p.DeletedAtUtc IS NULL AND pa.DeletedAtUtc IS NULL;
        """;

    public const string SoftDelete =
        """
        UPDATE dbo.ProviderAccount
        SET IsActive = 0,
            UpdatedAtUtc = SYSUTCDATETIME(),
            DeletedAtUtc = SYSUTCDATETIME(),
            DeletedByApiKeyId = @DeletedByApiKeyId
        WHERE Id = @Id AND DeletedAtUtc IS NULL;
        """;
}
