using SmsHubNext.Features.Tariffs;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Tariffs;

public sealed class QuoteTests : IAsyncLifetime
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
    public async Task Prices_a_gsm7_message_against_the_seeded_tariff()
    {
        var result = await new QuoteHandler(_db)
            .Handle(new QuoteRequest { ProviderId = 1, Text = "Hello" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsEncoding.Gsm7, result.Value.Encoding);
        Assert.Equal(1, result.Value.SegmentCount);
        Assert.Equal(1, result.Value.TariffId);
        Assert.Equal(1000.0000m, result.Value.UnitPrice);
        Assert.Equal(1000.0000m, result.Value.TotalCost);
    }

    [Fact]
    public async Task Returns_not_found_when_no_tariff_matches_the_encoding()
    {
        // Persian text is UCS-2; only a GSM-7 tariff is seeded.
        var result = await new QuoteHandler(_db)
            .Handle(new QuoteRequest { ProviderId = 1, Text = "سلام" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
