using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Database;

namespace SmsHubNext.IntegrationTests.Shared;

/// <summary>
/// Test-only reference data. Production migrations intentionally leave these tables empty so
/// the first-run wizard owns installation-specific configuration.
/// </summary>
public static class ReferenceDataTestData
{
    public static async Task EnsureDefaultsAsync(Db db)
    {
        await using SqlConnection connection = await db.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.MessageType WHERE Id = 1)
            BEGIN
                INSERT INTO dbo.MessageType (Id, Name, Code)
                VALUES (1, N'OTP', 'otp');
            END;

            SET IDENTITY_INSERT dbo.Provider ON;

            IF NOT EXISTS (SELECT 1 FROM dbo.Provider WHERE Id = 1)
            BEGIN
                INSERT INTO dbo.Provider (Id, Name, Code, BaseUrl, FallbackBaseUrl, IsActive)
                VALUES (1, N'Magfa Test', 'magfa', 'https://sms.magfa.com', NULL, 1);
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.Provider WHERE Id = 2)
            BEGIN
                INSERT INTO dbo.Provider (Id, Name, Code, BaseUrl, FallbackBaseUrl, IsActive)
                VALUES (2, N'Kavenegar Test', 'kavenegar', 'https://api.kavenegar.com', NULL, 1);
            END;

            SET IDENTITY_INSERT dbo.Provider OFF;
            """);
    }
}
