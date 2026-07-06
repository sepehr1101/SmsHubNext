using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.IntegrationTests.Shared;

public static class ProviderAccountTestData
{
    public static async Task<int> AssignActiveMagfaAccountToDefaultTestLineAsync(Db db)
    {
        await EnsureGsm7TariffAsync(db);
        await EnsureSenderLineAsync(db, "30001234");
        int providerAccountId = await CreateMagfaAccountAsync(db, isActive: true);
        await AssignSenderLineAsync(db, "30001234", providerAccountId);
        return providerAccountId;
    }

    public static async Task<int> CreateMagfaAccountAsync(Db db, bool isActive)
    {
        ISecretProtector protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        byte[] secretEncrypted = protector.Protect("secret-password");

        await using SqlConnection connection = await db.OpenConnectionAsync(CancellationToken.None);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.ProviderAccount
                (ProviderId, DisplayName, AuthType, SettingsJson, SecretEncrypted, IsActive)
            OUTPUT INSERTED.Id
            VALUES
                (1, N'Magfa Test Account', @AuthType, @SettingsJson, @SecretEncrypted, @IsActive);
            """,
            new
            {
                AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
                SettingsJson = """{"username":"user","domain":"domain"}""",
                SecretEncrypted = secretEncrypted,
                IsActive = isActive,
            },
            cancellationToken: CancellationToken.None));
    }

    public static async Task AssignSenderLineAsync(Db db, string lineNumber, int? providerAccountId)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(CancellationToken.None);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.SenderLine
            SET ProviderAccountId = @ProviderAccountId
            WHERE LineNumber = @LineNumber;
            """,
            new { ProviderAccountId = providerAccountId, LineNumber = lineNumber },
            cancellationToken: CancellationToken.None));
    }

    public static async Task EnsureSenderLineAsync(Db db, string lineNumber)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(CancellationToken.None);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.SenderLine WHERE LineNumber = @LineNumber)
            BEGIN
                INSERT INTO dbo.SenderLine (ProviderId, LineNumber, IsSharedLine, IsActive)
                VALUES (1, @LineNumber, 1, 1);
            END
            """,
            new { LineNumber = lineNumber },
            cancellationToken: CancellationToken.None));
    }

    public static async Task<int> EnsureGsm7TariffAsync(Db db)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(CancellationToken.None);

        int tariffId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM dbo.Tariff
                WHERE ProviderId = 1
                  AND MessageTypeId IS NULL
                  AND Encoding = 0
                  AND IsActive = 1
                  AND EffectiveToUtc IS NULL
            )
            BEGIN
                INSERT INTO dbo.Tariff
                    (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive)
                VALUES
                    (1, NULL, 0, '2025-01-01T00:00:00', NULL, 'IRR', 1);

                DECLARE @CreatedTariffId INT = CONVERT(INT, SCOPE_IDENTITY());

                INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
                VALUES (@CreatedTariffId, 0, NULL, 1000.0000);

                SELECT @CreatedTariffId;
            END
            ELSE
            BEGIN
                SELECT TOP (1) Id
                FROM dbo.Tariff
                WHERE ProviderId = 1
                  AND MessageTypeId IS NULL
                  AND Encoding = 0
                  AND IsActive = 1
                  AND EffectiveToUtc IS NULL
                ORDER BY Id;
            END
            """,
            cancellationToken: CancellationToken.None));

        return tariffId;
    }
}
