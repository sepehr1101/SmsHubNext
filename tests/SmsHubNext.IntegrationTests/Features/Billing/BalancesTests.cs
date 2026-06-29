using DbUp.Engine;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Billing;

public sealed class BalancesTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder().Build();
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

    private async Task<short> CreateCustomerAsync(string code)
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = code, Code = code }, CancellationToken.None);
        Assert.True(customer.IsSuccess);
        return customer.Value.Id;
    }

    [Fact]
    public async Task Top_up_accumulates_and_is_reflected_in_the_balance()
    {
        short id = await CreateCustomerAsync("payer");
        TopUpHandler topUp = new TopUpHandler(_db);

        Result<TopUpResponse> first = await topUp.Handle(new TopUpRequest { CustomerId = id, Amount = 1000m }, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(1000m, first.Value.Balance);

        Result<TopUpResponse> second = await topUp.Handle(
            new TopUpRequest { CustomerId = id, Amount = 500m, Reference = "pay-1" }, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(1500m, second.Value.Balance);

        Result<CustomerBalance> balance = await new GetBalanceHandler(_db, TimeProvider.System).Handle(id, CancellationToken.None);
        Assert.True(balance.IsSuccess);
        Assert.Equal(1500m, balance.Value.Balance);
    }

    [Fact]
    public async Task Top_up_for_an_unknown_customer_is_rejected()
    {
        Result<TopUpResponse> result = await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = 32000, Amount = 100m }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Get_balance_with_no_prior_top_up_is_zero()
    {
        short id = await CreateCustomerAsync("fresh");

        Result<CustomerBalance> balance = await new GetBalanceHandler(_db, TimeProvider.System).Handle(id, CancellationToken.None);

        Assert.True(balance.IsSuccess);
        Assert.Equal(0m, balance.Value.Balance);
    }
}
