using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class CreateGeoSectionTests : IAsyncLifetime
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
    public async Task Creates_a_child_with_a_materialized_path()
    {
        // Seeded province Tehran has Id 1 and Path '/1/'.
        var created = await new CreateGeoSectionHandler(_db).Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = 1,
                SectionType = GeoSectionType.City,
                Name = "Rey",
                Code = "THR-REY",
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.StartsWith("/1/", created.Value.Path);
        Assert.EndsWith($"/{created.Value.Id}/", created.Value.Path);
    }

    [Fact]
    public async Task Rejects_an_unknown_parent()
    {
        var created = await new CreateGeoSectionHandler(_db).Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = 9999,
                SectionType = GeoSectionType.City,
                Name = "Nowhere",
                Code = "NWH",
            },
            CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorType.Validation, created.Error!.Type);
    }
}
