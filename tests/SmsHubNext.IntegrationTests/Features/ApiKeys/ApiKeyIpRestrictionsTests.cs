using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ApiKeys;

public sealed class ApiKeyIpRestrictionsTests : IAsyncLifetime
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
    public async Task Adds_then_lists_a_restriction()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Tenant", Code = "tenant" }, CancellationToken.None);
        Result<IssueApiKeyResponse> apiKey = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Key" }, CancellationToken.None);
        Assert.True(apiKey.IsSuccess);

        Result<ApiKeyIpRestriction> added = await new AddIpRestrictionHandler(_db).Handle(
            apiKey.Value.Id,
            new AddIpRestrictionRequest { Cidr = "10.0.0.0/24", Description = "intranet" },
            CancellationToken.None);
        Assert.True(added.IsSuccess);

        Result<IReadOnlyList<ApiKeyIpRestriction>> listed = await new ListIpRestrictionsHandler(_db).Handle(apiKey.Value.Id, CancellationToken.None);
        Assert.True(listed.IsSuccess);
        ApiKeyIpRestriction restriction = Assert.Single(listed.Value);
        Assert.Equal("10.0.0.0/24", restriction.Cidr);
        Assert.Equal("intranet", restriction.Description);
    }
}
