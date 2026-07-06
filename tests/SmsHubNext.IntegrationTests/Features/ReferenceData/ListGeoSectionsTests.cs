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

public sealed class ListGeoSectionsTests : IAsyncLifetime
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
    public async Task Returns_created_sections()
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

        Result<CreateGeoSectionResponse> city = await new CreateGeoSectionHandler(_db).Handle(
            new CreateGeoSectionRequest
            {
                ParentGeoSectionId = province.Value.Id,
                SectionType = GeoSectionType.City,
                Name = "Tehran",
                Code = "THR-01",
            },
            CancellationToken.None);
        Assert.True(city.IsSuccess, city.Error?.Message);

        Result<IReadOnlyList<GeoSection>> result = await new ListGeoSectionsHandler(_db).Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, s =>
            s.Code == "THR" && s.SectionType == GeoSectionType.Province && s.ParentGeoSectionId == null);
        Assert.Contains(result.Value, s =>
            s.Code == "THR-01" && s.SectionType == GeoSectionType.City && s.ParentGeoSectionId == province.Value.Id);
    }
}
