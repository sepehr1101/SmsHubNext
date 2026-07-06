using DbUp.Engine;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class CreateGeoSectionTests : IAsyncLifetime
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
    public async Task Creates_a_child_with_a_materialized_path()
    {
        Result<CreateGeoSectionResponse> province = await new CreateGeoSectionHandler(_db).Handle(
            new CreateGeoSectionRequest
            {
                SectionType = GeoSectionType.Province,
                Name = "Tehran",
                Code = "THR",
            },
            CancellationToken.None);
        Assert.True(province.IsSuccess, province.Error?.Message);

        Result<CreateGeoSectionResponse> created = await new CreateGeoSectionHandler(_db).Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = province.Value.Id,
                SectionType = GeoSectionType.City,
                Name = "Rey",
                Code = "THR-REY",
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.StartsWith($"/{province.Value.Id}/", created.Value.Path);
        Assert.EndsWith($"/{created.Value.Id}/", created.Value.Path);
    }

    [Fact]
    public async Task Rejects_an_unknown_parent()
    {
        Result<CreateGeoSectionResponse> created = await new CreateGeoSectionHandler(_db).Handle(
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
