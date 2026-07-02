using DbUp.Engine;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

/// <summary>Migrates a real SQL Server (Testcontainers) and reads the seeded providers.</summary>
public sealed class ListProvidersTests : IAsyncLifetime
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
    public async Task Returns_the_seeded_providers()
    {
        ListProvidersHandler handler = new ListProvidersHandler(_db);

        Result<IReadOnlyList<Provider>> result = await handler.Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, p => p.Id == 1 && p.Code == "magfa" && p.Name == "Magfa" && p.IsActive);
    }
}
