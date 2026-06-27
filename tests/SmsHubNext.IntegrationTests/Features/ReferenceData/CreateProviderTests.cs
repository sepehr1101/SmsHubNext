using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class CreateProviderTests : IAsyncLifetime
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
    public async Task Creates_a_provider_then_lists_it()
    {
        var created = await new CreateProviderHandler(_db).Handle(
            new CreateProviderRequest { Name = "Kavenegar", Code = "kavenegar", BaseUrl = "https://api.kavenegar.com" },
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.True(created.Value.Id > 1); // Magfa is seeded at Id 1

        var listed = await new ListProvidersHandler(_db).Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, p => p.Code == "kavenegar");
    }
}
