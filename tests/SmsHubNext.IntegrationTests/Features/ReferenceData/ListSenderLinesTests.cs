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

/// <summary>Migrates a real SQL Server (Testcontainers) and reads the seeded sender lines.</summary>
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
    public async Task Returns_the_seeded_sender_lines()
    {
        ListSenderLinesHandler handler = new ListSenderLinesHandler(_db);

        Result<IReadOnlyList<SenderLine>> result = await handler.Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.All(result.Value, line => Assert.Equal((byte)1, line.ProviderId));
        Assert.Contains(result.Value, line => line.LineNumber == "10001234" && !line.IsSharedLine);
    }
}
