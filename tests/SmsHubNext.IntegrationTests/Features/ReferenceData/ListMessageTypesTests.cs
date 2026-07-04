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

/// <summary>
/// Migrates a real SQL Server (Testcontainers — requires Docker) and reads the
/// seeded message types back through the handler.
/// </summary>
public sealed class ListMessageTypesTests : IAsyncLifetime
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
    public async Task Returns_the_seeded_message_types()
    {
        ListMessageTypesHandler handler = new ListMessageTypesHandler(_db);

        Result<IReadOnlyList<MessageType>> result = await handler.Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.Contains(result.Value, t => t.Id == 4 && t.Code == "water-bill" && t.Name == "Water Bill");
    }

    [Fact]
    public async Task Migration_is_idempotent_when_run_again()
    {
        DatabaseUpgradeResult second = new DatabaseMigrator(_sqlServer.GetConnectionString()).Migrate();

        Assert.True(second.Successful);
    }
}
