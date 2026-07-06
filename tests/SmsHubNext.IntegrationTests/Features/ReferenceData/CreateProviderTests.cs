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

public sealed class CreateProviderTests : IAsyncLifetime
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
    public async Task Creates_a_provider_then_lists_it()
    {
        Result<CreateProviderResponse> created = await new CreateProviderHandler(_db).Handle(
            new CreateProviderRequest { Name = "Test Provider", Code = $"provider-{Guid.NewGuid():N}", BaseUrl = "https://api.example.test" },
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.True(created.Value.Id > 2); // Magfa and Kavenegar are seeded

        Result<IReadOnlyList<Provider>> listed = await new ListProvidersHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, p => p.Name == "Test Provider");
    }
}
