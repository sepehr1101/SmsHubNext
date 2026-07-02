using DbUp.Engine;
using SmsHubNext.Features.Tariffs;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Tariffs;

public sealed class ListTariffsTests : IAsyncLifetime
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
    public async Task Returns_the_seeded_tariff_with_its_rates()
    {
        Result<IReadOnlyList<TariffResponse>> result = await new ListTariffsHandler(_db).Handle(CancellationToken.None);

        Assert.True(result.IsSuccess);
        TariffResponse tariff = Assert.Single(result.Value);
        Assert.Equal(SmsEncoding.Gsm7, tariff.Encoding);
        Assert.Equal("IRR", tariff.Currency);

        TariffRate rate = Assert.Single(tariff.Rates);
        Assert.Equal(1000.0000m, rate.PricePerSegment);
    }
}
