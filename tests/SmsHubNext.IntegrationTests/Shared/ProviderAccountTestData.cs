using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.IntegrationTests.Shared;

public static class ProviderAccountTestData
{
    public static async Task<int> AssignActiveMagfaAccountToSeededLineAsync(Db db)
    {
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
}
