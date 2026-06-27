using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Shared.Database;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ApiKeys;

public sealed class ApiKeyIpRestrictionsTests : IAsyncLifetime
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
    public async Task Adds_then_lists_a_restriction()
    {
        var customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "Tenant", Code = "tenant" }, CancellationToken.None);
        var apiKey = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Key" }, CancellationToken.None);
        Assert.True(apiKey.IsSuccess);

        var added = await new AddIpRestrictionHandler(_db).Handle(
            apiKey.Value.Id,
            new AddIpRestrictionRequest { Cidr = "10.0.0.0/24", Description = "intranet" },
            CancellationToken.None);
        Assert.True(added.IsSuccess);

        var listed = await new ListIpRestrictionsHandler(_db).Handle(apiKey.Value.Id, CancellationToken.None);
        Assert.True(listed.IsSuccess);
        var restriction = Assert.Single(listed.Value);
        Assert.Equal("10.0.0.0/24", restriction.Cidr);
        Assert.Equal("intranet", restriction.Description);
    }
}
