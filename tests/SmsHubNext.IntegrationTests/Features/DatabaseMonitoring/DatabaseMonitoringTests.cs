using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.DatabaseMonitoring;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.DatabaseMonitoring;

public sealed class DatabaseMonitoringFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _server = new MsSqlBuilder(Literals.sqlImage).Build();
    public Db Db { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await _server.StartAsync();
        Db = new Db(_server.GetConnectionString());
        await using SqlConnection connection = await Db.OpenConnectionAsync();
        // Query Store is unsupported on master; use a dedicated database for this fixture.
        await connection.ExecuteAsync("CREATE DATABASE MonitorTest;");
        SqlConnectionStringBuilder builder = new(_server.GetConnectionString()) { InitialCatalog = "MonitorTest" };
        Db = new Db(builder.ConnectionString);
        DatabaseUpgradeResult migration = new DatabaseMigrator(builder.ConnectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
    }
    public Task DisposeAsync() => _server.DisposeAsync().AsTask();
}

public sealed class DatabaseMonitoringTests(DatabaseMonitoringFixture fixture) : IClassFixture<DatabaseMonitoringFixture>
{
    private DatabaseMonitoringHandler Handler => new(fixture.Db, TimeProvider.System, new DatabaseMonitoringCollectorState());
    [Theory]
    [InlineData(DatabaseMonitorSection.Overview)]
    [InlineData(DatabaseMonitorSection.Tables)]
    [InlineData(DatabaseMonitorSection.Indexes)]
    [InlineData(DatabaseMonitorSection.Statistics)]
    [InlineData(DatabaseMonitorSection.Requests)]
    [InlineData(DatabaseMonitorSection.Operations)]
    [InlineData(DatabaseMonitorSection.History)]
    [InlineData(DatabaseMonitorSection.Alerts)]
    public async Task Reads_every_diagnostic_query_on_real_sql_server(DatabaseMonitorSection section)
    {
        Result<DatabaseMonitorReport> result = await Handler.Read(section, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        if (section is DatabaseMonitorSection.Overview or DatabaseMonitorSection.Tables or DatabaseMonitorSection.Indexes or DatabaseMonitorSection.Statistics) Assert.NotEmpty(result.Value.Rows);
    }
    [Fact]
    public async Task Snapshot_is_daily_idempotent_and_growth_is_not_fabricated()
    {
        await Handler.Collect(default);
        await Handler.Collect(default);
        IReadOnlyList<DatabaseGrowthPoint> growth = (await Handler.Growth(default)).Value;
        Assert.Single(growth);
        Assert.True(growth[0].DataMb>0);
        await using SqlConnection connection = await fixture.Db.OpenConnectionAsync();
        long duplicates = await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM (SELECT ObjectId FROM dbo.DatabaseTableSnapshot GROUP BY SnapshotDateUtc,ObjectId HAVING COUNT(*)>1) x;");
        Assert.Equal(0, duplicates);
    }
    [Fact]
    public async Task Inspects_only_the_selected_index_partition()
    {
        DatabaseReading row = (await Handler.Read(DatabaseMonitorSection.Indexes, default)).Value.Rows.First();
        Result<DatabaseMonitorReport> result = await Handler.InspectIndex(row.ObjectId!.Value,row.IndexId!.Value,row.PartitionNumber!.Value,default);
        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Rows, item => { Assert.Equal(row.ObjectId,item.ObjectId); Assert.Equal(row.IndexId,item.IndexId); Assert.Equal(row.PartitionNumber,item.PartitionNumber); Assert.NotNull(item.Metric3); });
        Assert.True((await Handler.InspectIndex(0,0,0,default)).IsFailure);
        Assert.True((await Handler.InspectIndex(row.ObjectId.Value,row.IndexId.Value,int.MaxValue,default)).IsFailure);
    }
    [Fact]
    public async Task Query_store_is_not_enabled_automatically_and_enabled_query_executes()
    {
        await using SqlConnection connection = await fixture.Db.OpenConnectionAsync();
        await connection.ExecuteAsync("ALTER DATABASE CURRENT SET QUERY_STORE = OFF;");
        Result<DatabaseMonitorReport> off = await Handler.Read(DatabaseMonitorSection.Queries,default);
        Assert.True(off.IsSuccess);
        Assert.NotNull(off.Value.Notice);
        await connection.ExecuteAsync("ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);");
        for (int run=0;run<5;run++) await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM dbo.Customer;");
        await connection.ExecuteAsync("EXEC sys.sp_query_store_flush_db;");
        Result<DatabaseMonitorReport> on = await Handler.Read(DatabaseMonitorSection.Queries,default);
        Assert.True(on.IsSuccess);
        Assert.NotEmpty(on.Value.Rows);
        for(int sort=1;sort<=3;sort++)
            Assert.NotEmpty((await Handler.Read(DatabaseMonitorSection.Queries,default,sort)).Value.Rows);
        await connection.ExecuteAsync("ALTER DATABASE CURRENT SET QUERY_STORE = OFF;");
    }
    [Fact]
    public async Task Missing_permissions_are_reported_as_unavailable_instead_of_zero()
    {
        await using SqlConnection connection = await fixture.Db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            CREATE LOGIN MonitorLimited WITH PASSWORD='Monitor-Only_Test!123';
            CREATE USER MonitorLimited FOR LOGIN MonitorLimited;
            GRANT SELECT ON SCHEMA::dbo TO MonitorLimited;
            """);
        SqlConnectionStringBuilder builder = new(connection.ConnectionString) { UserID = "MonitorLimited", Password = "Monitor-Only_Test!123" };
        DatabaseMonitoringHandler limited = new(new Db(builder.ConnectionString),TimeProvider.System,new DatabaseMonitoringCollectorState());
        Result<DatabaseMonitorReport> result = await limited.Read(DatabaseMonitorSection.Overview,default);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden,result.Error!.Type);
        Assert.Equal(ErrorType.Forbidden,(await limited.Read(DatabaseMonitorSection.Requests,default)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden,(await limited.Read(DatabaseMonitorSection.Alerts,default)).Error!.Type);
    }    [Fact]
    public async Task History_preserves_real_observation_times_and_does_not_infer_completion()
    {
        await using SqlConnection connection=await fixture.Db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            INSERT dbo.DatabaseIndexObservation (ObjectId,IndexId,StartedAtServerTime,IndexName,TableName,FirstObservedAtUtc,LastObservedAtUtc,StateDescription,PercentComplete,ExecutionMinutes)
            VALUES (1234567,1,SYSDATETIME(),N'test-index',N'[dbo].[test-history]',DATEADD(MINUTE,-5,SYSUTCDATETIME()),SYSUTCDATETIME(),N'متوقف‌شده',35.5,4);
            """);
        DatabaseReading row=(await Handler.Read(DatabaseMonitorSection.History,default)).Value.Rows.Single(item=>item.Detail=="test-index");
        Assert.Equal(35.5m,row.Metric1);
        Assert.Equal(4m,row.Metric2);
        Assert.True(row.FirstObservedAtUtc<row.AtUtc);
        Assert.Contains("متوقف‌شده",row.Status);
        Assert.DoesNotContain("موفق",row.Status);
    }}





