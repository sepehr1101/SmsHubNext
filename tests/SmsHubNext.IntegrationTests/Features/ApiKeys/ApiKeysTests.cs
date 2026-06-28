using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ApiKeys;

public sealed class ApiKeysTests : IAsyncLifetime
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

    [Fact]
    public async Task Issues_a_key_then_lists_it_without_the_secret()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Tenant", Code = "tenant" }, CancellationToken.None);
        Assert.True(customer.IsSuccess);

        Result<IssueApiKeyResponse> issued = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Prod" }, CancellationToken.None);
        Assert.True(issued.IsSuccess);
        Assert.StartsWith("shn_", issued.Value.Key);
        Assert.Equal(issued.Value.Key[..12], issued.Value.KeyPrefix);

        Result<IReadOnlyList<ApiKey>> keys = await new ListApiKeysHandler(_db).Handle(customer.Value.Id, CancellationToken.None);
        Assert.True(keys.IsSuccess);
        ApiKey key = Assert.Single(keys.Value);
        Assert.Equal(issued.Value.Id, key.Id);
        Assert.Equal("Prod", key.Name);
        Assert.Equal(issued.Value.KeyPrefix, key.KeyPrefix);
    }

    [Fact]
    public async Task Rejects_an_unknown_customer()
    {
        Result<IssueApiKeyResponse> result = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = 32000, Name = "Nope" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
