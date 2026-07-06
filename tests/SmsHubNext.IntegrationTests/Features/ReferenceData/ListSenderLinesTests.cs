using DbUp.Engine;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

/// <summary>Migrates a real SQL Server (Testcontainers) and reads configured sender lines.</summary>
public sealed class ListSenderLinesTests : IAsyncLifetime
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
    public async Task Returns_configured_sender_lines()
    {
        await ProviderAccountTestData.EnsureSenderLineAsync(_db, "30001234");

        ListSenderLinesHandler handler = new ListSenderLinesHandler(_db);

        Result<IReadOnlyList<SenderLine>> result = await handler.Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        SenderLine senderLine = Assert.Single(result.Value);
        Assert.Equal("30001234", senderLine.LineNumber);
        Assert.All(result.Value, line => Assert.Equal((byte)1, line.ProviderId));
    }
}
