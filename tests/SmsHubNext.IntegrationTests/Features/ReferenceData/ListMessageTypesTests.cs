using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

/// <summary>
/// Migrates a real SQL Server (Testcontainers — requires Docker) and reads the
/// seeded message types back through the handler.
/// </summary>
public sealed class ListMessageTypesTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder().Build();
    private Db _db = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        var connectionString = _sqlServer.GetConnectionString();

        var migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_the_seeded_message_types()
    {
        var handler = new ListMessageTypesHandler(_db);

        var result = await handler.Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.Contains(result.Value, t => t.Id == 4 && t.Code == "water-bill" && t.Name == "Water Bill");
    }

    [Fact]
    public async Task Migration_is_idempotent_when_run_again()
    {
        var second = new DatabaseMigrator(_sqlServer.GetConnectionString()).Migrate();

        Assert.True(second.Successful);
    }
}
