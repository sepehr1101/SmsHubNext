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
        await seeder.Seed(CancellationToken.None);

        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);
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
