using System.Net;
using Dapper;
using DbUp.Engine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using SmsHubNext.Deployment;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Deployment;

public sealed class DevelopmentDataSeederTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();
        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        _db = new Db(connectionString);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Repeated_seed_creates_one_4040_esb_line_and_accepts_persian_and_latin_test_messages()
    {
        ISecretProtector protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        DevelopmentDataSeeder seeder = new(_db, protector);
        await seeder.Seed(CancellationToken.None);
        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);
        byte originalBillTypeId = await connection.QuerySingleAsync<byte>("SELECT Id FROM dbo.MessageType WHERE Code = 'water_bill';");
        await connection.ExecuteAsync("""
            INSERT INTO dbo.MessageType (Id, Name, Code, IsActive)
            SELECT CONVERT(TINYINT, MAX(CONVERT(INT, Id)) + 1), N'نوع پیام قبلی', 'obsolete_seed_type', 1
            FROM dbo.MessageType;
            """);
        await seeder.Seed(CancellationToken.None);

        Assert.Equal(235, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GeoSection WHERE DeletedAtUtc IS NULL;"));
        Assert.Equal(195, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.GeoSection zone
            INNER JOIN dbo.GeoSection region ON region.Id = zone.ParentGeoSectionId
            INNER JOIN dbo.GeoSection province ON province.Id = region.ParentGeoSectionId
            WHERE zone.SectionType = 3 AND region.SectionType = 2 AND province.Code = 'isfahan'
                AND zone.Path = CONCAT(region.Path, zone.Id, '/');
            """));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.GeoSection zone
            INNER JOIN dbo.GeoSection region ON region.Id = zone.ParentGeoSectionId
            WHERE zone.Code = '131301' AND region.Code = 'region-131301';
            """));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.GeoSection
            WHERE Code IN ('tehran', 'shemiranat', 'afjeh') AND DeletedAtUtc IS NULL;
            """));

        string[] expectedTypeNames = ["قبض", "وصول مطالبات آب بها", "وصول مطالبات انشعاب", "اطلاع رسانی", "OTP", "فرایند خدمات", "واحد gis"];
        string[] actualTypeNames = (await connection.QueryAsync<string>(
            "SELECT Name FROM dbo.MessageType WHERE IsActive = 1 AND DeletedAtUtc IS NULL;")).ToArray();
        Assert.Equal(expectedTypeNames.OrderBy(name => name), actualTypeNames.OrderBy(name => name));
        Assert.Equal(originalBillTypeId, await connection.QuerySingleAsync<byte>("SELECT Id FROM dbo.MessageType WHERE Code = 'water_bill';"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.MessageType
            WHERE Code = 'obsolete_seed_type' AND IsActive = 0
                AND DeletedAtUtc IS NOT NULL AND DeletedByApiKeyId IS NOT NULL;
            """));
        string[] catalogCodes = ["1000", "2000", "5000", "asanak", "farazsms", "ghasedak", "kavenegar", "smsir"];
        foreach (string code in catalogCodes)
        {
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Provider WHERE Code = @Code AND IsActive = 0 AND DeletedAtUtc IS NULL;",
                new { Code = code }));
        }
        byte providerId = await connection.QuerySingleAsync<byte>("SELECT Id FROM dbo.Provider WHERE Code = '4040';");
        Result<IReadOnlyList<SenderLine>> lines = await new ListSenderLinesHandler(_db).Handle(CancellationToken.None);
        SenderLine line = Assert.Single(lines.Value, item => item.LineNumber == "40401403");
        Assert.Equal(providerId, line.ProviderId);
        Assert.True(line.UseEsb);
        Assert.True(line.IsActive);
        Assert.Null(line.ProviderAccountId);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Tariff WHERE ProviderId = @ProviderId AND MessageTypeId IS NULL;",
            new { ProviderId = providerId }));

        Result<ApiKeyIdentity> identity = await new ApiKeyAuthenticator(_db, TimeProvider.System).Authenticate(
            DevelopmentDataSeeder.CustomerApiKey, IPAddress.Loopback, CancellationToken.None);
        Assert.True(identity.IsSuccess, identity.Error?.Message);
        byte messageTypeId = await connection.QuerySingleAsync<byte>("SELECT Id FROM dbo.MessageType WHERE Code = 'water_bill';");
        SendMessagesHandler send = new(_db, TimeProvider.System, new EsbOptions { Enabled = true });
        Result<SendMessagesResponse> accepted = await send.Handle(new SendMessagesRequest
        {
            SenderLine = "40401403", MessageTypeId = messageTypeId, ClientBatchId = "seed-4040-accept",
            Messages =
            [
                new SendMessageItem { Recipient = "09121234567", Text = "سلام" },
                new SendMessageItem { Recipient = "09121234568", Text = "Hello" },
            ],
        }, identity.Value, CancellationToken.None);

        // Acceptance only, with no dispatcher or upstream calls in this test.
        Assert.True(accepted.IsSuccess, accepted.Error?.Message);
        Assert.Equal(2, accepted.Value.AcceptedCount);
        Assert.Equal(3000m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT TotalCost FROM dbo.MessageBatch WHERE Id = @Id;", new { Id = accepted.Value.BatchId }));
    }
}
