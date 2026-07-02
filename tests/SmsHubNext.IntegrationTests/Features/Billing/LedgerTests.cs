using DbUp.Engine;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Billing;

public sealed class LedgerTests : IAsyncLifetime
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
    public async Task Records_a_ledger_entry_per_top_up()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Ledger", Code = "ledger" }, CancellationToken.None);
        short id = customer.Value.Id;

        TopUpHandler topUp = new TopUpHandler(_db);
        await topUp.Handle(new TopUpRequest { CustomerId = id, Amount = 1000m }, CancellationToken.None);
        await topUp.Handle(new TopUpRequest { CustomerId = id, Amount = 500m, Reference = "pay-2" }, CancellationToken.None);

        Result<IReadOnlyList<BalanceTransaction>> ledger = await new ListTransactionsHandler(_db).Handle(id, CancellationToken.None);

        Assert.True(ledger.IsSuccess);
        Assert.Equal(2, ledger.Value.Count);
        Assert.All(ledger.Value, entry => Assert.Equal(BalanceTransactionType.TopUp, entry.Type));
        Assert.Equal(1500m, ledger.Value[^1].BalanceAfter);
    }
}
