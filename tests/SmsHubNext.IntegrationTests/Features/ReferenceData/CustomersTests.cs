using DbUp.Engine;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.ReferenceData;

public sealed class CustomersTests : IAsyncLifetime
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
    public async Task Creates_then_lists_a_customer()
    {
        CreateCustomerHandler create = new CreateCustomerHandler(_db);
        ListCustomersHandler list = new ListCustomersHandler(_db);

        Result<CreateCustomerResponse> created = await create.Handle(
            new CreateCustomerRequest { Name = "Acme Water", Code = "acme" }, CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.True(created.Value.Id > 0);

        Result<IReadOnlyList<Customer>> listed = await list.Handle(CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, c =>
            c.Id == created.Value.Id && c.Code == "acme" && c.Name == "Acme Water" && c.IsActive);
    }

    [Fact]
    public async Task Rejects_a_duplicate_code_as_conflict()
    {
        CreateCustomerHandler create = new CreateCustomerHandler(_db);

        Result<CreateCustomerResponse> first = await create.Handle(
            new CreateCustomerRequest { Name = "First", Code = "dup" }, CancellationToken.None);
        Assert.True(first.IsSuccess);

        Result<CreateCustomerResponse> second = await create.Handle(
            new CreateCustomerRequest { Name = "Second", Code = "dup" }, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
    }
}
