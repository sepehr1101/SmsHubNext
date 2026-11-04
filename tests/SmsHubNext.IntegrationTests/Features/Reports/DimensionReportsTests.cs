using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Reports;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Reports;

public sealed class DimensionReportsFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _server = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    public Db Database => _db;
    public async Task InitializeAsync()
    {
        await _server.StartAsync();
        _db = new Db(_server.GetConnectionString());
        DatabaseUpgradeResult migration = new DatabaseMigrator(_server.GetConnectionString()).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        int tariff = await ProviderAccountTestData.EnsureGsm7TariffAsync(_db);
        await ProviderAccountTestData.EnsureSenderLineAsync(_db, "30001234");
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            INSERT dbo.Customer (Name, Code) VALUES (N'اول', 'first'), (N'دوم', 'second');
            INSERT dbo.ApiKey (CustomerId, Name, KeyPrefix, KeyHash)
            VALUES (1, N'test', 'test', HASHBYTES('SHA2_256', 'test'));
            INSERT dbo.MessageType (Id, Name, Code) VALUES (2, N'قبض', 'bill');
            SET IDENTITY_INSERT dbo.GeoSection ON;
            INSERT dbo.GeoSection (Id, ParentGeoSectionId, SectionType, Name, Code, Path) VALUES
                (1,NULL,1,N'استان اول','p1','/1/'), (2,1,2,N'شهر مشترک','c1','/1/2/'), (3,2,3,N'ناحیه','z1','/1/2/3/'),
                (10,NULL,1,N'استان دوم','p2','/10/'), (11,10,2,N'شهر مشترک','c2','/10/11/'), (12,11,3,N'ناحیه','z2','/10/11/12/');
            SET IDENTITY_INSERT dbo.GeoSection OFF;
            DECLARE @Line SMALLINT = (SELECT Id FROM dbo.SenderLine WHERE LineNumber = '30001234');
            INSERT dbo.MessageBatch (SubmitDateJalali, CustomerId, ApiKeyId, SenderLineId, ProviderId, MessageCount, SegmentCount, TotalCost, Status)
                VALUES ('1405/01/01',1,1,@Line,1,5,6,150.1234,1), ('1405/02/01',2,1,@Line,1,1,1,60,1);
            INSERT dbo.Message (SubmitDateJalali, MessageBatchId, CustomerId, ProviderId, SenderLineId, MessageTypeId, GeoSectionId, MobileNumber, Encoding, CharacterCount, SegmentCount, TariffId, UnitPrice, TotalCost, Status, DeliveryStatus)
            SELECT d, b, c, 1, @Line, t, g, '989120000001', 0, 10, s, @Tariff, cost/s, cost, status, delivery
            FROM (VALUES
                ('1405/01/10',1,1,1,3,2,CAST(10.1234 AS DECIMAL(19,4)),2,2),
                ('1405/02/10',1,1,1,2,1,20,3,3),
                ('1405/02/15',1,1,2,3,1,30,2,1),
                ('1405/02/20',1,1,2,NULL,1,40,1,1),
                ('1405/01/20',1,1,1,1,1,50,4,1),
                ('1405/02/10',2,2,1,12,1,60,2,2),
                ('1405/03/01',1,1,1,3,1,70,2,2)
            ) source(d,b,c,t,g,s,cost,status,delivery);
            -- Deleted dimensions still resolve historical labels and remain usable as report filters.
            UPDATE dbo.MessageType SET DeletedAtUtc = SYSUTCDATETIME(), DeletedByApiKeyId = 1 WHERE Id = 2;
            UPDATE dbo.GeoSection SET DeletedAtUtc = SYSUTCDATETIME(), DeletedByApiKeyId = 1 WHERE Id = 3;
            """, new { Tariff = tariff });
    }
    public Task DisposeAsync() => _server.DisposeAsync().AsTask();
}

public sealed class DimensionReportsTests(DimensionReportsFixture fixture) : IClassFixture<DimensionReportsFixture>
{
    private readonly Db _db = fixture.Database;
    private Task<Result<IReadOnlyList<DimensionReportRow>>> Report(DimensionReportKind kind, byte level = 1, MessageReportRequest? filters = null) =>
        new DimensionReportsHandler(_db).Handle(new() { Kind = kind, GeographyLevel = level, Filters = filters ?? new() { FromJalali = "1405/01/01", ToJalali = "1405/02/31" } }, default);

    [Theory]
    [InlineData(DimensionReportKind.Geography)]
    [InlineData(DimensionReportKind.MessageType)]
    [InlineData(DimensionReportKind.GeographyMessageType)]
    [InlineData(DimensionReportKind.GeographyMonth)]
    [InlineData(DimensionReportKind.MessageTypeMonth)]
    public async Task All_five_groupings_preserve_totals_and_snapshot_costs(DimensionReportKind kind)
    {
        Result<IReadOnlyList<DimensionReportRow>> result = await Report(kind);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(6, result.Value.Sum(row => row.MessageCount));
        Assert.Equal(7, result.Value.Sum(row => row.SegmentCount));
        Assert.Equal(210.1234m, result.Value.Sum(row => row.TotalCost));
        Assert.Equal(4, result.Value.Sum(row => row.AcceptedCount));
        Assert.Equal(2, result.Value.Sum(row => row.DeliveredCount));
        Assert.Equal(1, result.Value.Sum(row => row.UndeliveredCount));
        Assert.Equal(1, result.Value.Sum(row => row.PendingCount));
    }

    [Fact]
    public async Task Province_rollup_includes_descendants_once_and_keeps_unassigned()
    {
        IReadOnlyList<DimensionReportRow> rows = (await Report(DimensionReportKind.Geography)).Value;
        DimensionReportRow province = Assert.Single(rows, row => row.GeoSectionId == 1);
        Assert.Equal(4, province.MessageCount);
        Assert.Equal(110.1234m, province.TotalCost);
        Assert.Equal(33.3333m, province.DeliveryRate);
        Assert.Equal(1, Assert.Single(rows, row => row.GeoSectionId is null).MessageCount);
        Assert.Equal(1, Assert.Single(rows, row => row.GeoSectionId == 10).MessageCount);
        IReadOnlyList<DimensionReportRow> cities = (await Report(DimensionReportKind.Geography, 2)).Value;
        Assert.Equal(3, Assert.Single(cities, row => row.GeoSectionId == 2).MessageCount);
        Assert.Equal(2, Assert.Single(cities, row => row.GeoSectionId is null).MessageCount);
        Assert.NotEqual(cities.Single(row => row.GeoSectionId == 2).GeoName, cities.Single(row => row.GeoSectionId == 11).GeoName);
    }

    [Fact]
    public async Task Matrix_and_monthly_reports_group_by_both_dimensions()
    {
        IReadOnlyList<DimensionReportRow> matrix = (await Report(DimensionReportKind.GeographyMessageType)).Value;
        Assert.Equal(80.1234m, matrix.Single(row => row.GeoSectionId == 1 && row.MessageTypeId == 1).TotalCost);
        Assert.Equal(30, matrix.Single(row => row.GeoSectionId == 1 && row.MessageTypeId == 2).TotalCost);
        IReadOnlyList<DimensionReportRow> geoMonths = (await Report(DimensionReportKind.GeographyMonth)).Value;
        Assert.Equal(2, geoMonths.Single(row => row.GeoSectionId == 1 && row.JalaliMonth == "1405/01").MessageCount);
        IReadOnlyList<DimensionReportRow> typeMonths = (await Report(DimensionReportKind.MessageTypeMonth)).Value;
        Assert.Equal(2, typeMonths.Single(row => row.MessageTypeId == 2 && row.JalaliMonth == "1405/02").MessageCount);
    }

    [Fact]
    public async Task Filters_limit_tenant_provider_type_subtree_and_date()
    {
        MessageReportRequest filters = new() { FromJalali = "1405/02/01", ToJalali = "1405/02/31", CustomerId = 1, ProviderId = 1, MessageTypeId = 2, GeoSectionId = 1 };
        DimensionReportRow row = Assert.Single((await Report(DimensionReportKind.GeographyMonth, filters: filters)).Value);
        Assert.Equal(30, row.TotalCost);
        Assert.Equal(1, row.PendingCount);
        Assert.Equal(0, row.DeliveryRate);
        filters = new() { FromJalali = "1405/01/01", ToJalali = "1405/02/31", ProviderId = 2 };
        Assert.Empty((await Report(DimensionReportKind.MessageType, filters: filters)).Value);
    }

    [Fact]
    public async Task Lookup_preserves_deleted_labels_and_full_geography_paths()
    {
        DimensionReportOptions options = (await new DimensionReportsHandler(_db).Options(default)).Value;
        Assert.Contains("حذف‌شده", options.MessageTypes.Single(option => option.Id == 2).Name);
        Assert.Contains("استان اول / شهر مشترک / ناحیه", options.Geography.Single(option => option.Id == 3).Name);
    }
}

