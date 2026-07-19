using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Setup;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Setup;

public sealed class FactoryResetTests : IAsyncLifetime
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
    public async Task Restores_a_fresh_install_when_no_sms_exists()
    {
        short customerId = await CreateCustomerAsync("resettable");
        await IssueApiKeyAsync(customerId);
        await new TopUpHandler(_db).Handle(
            new TopUpRequest { CustomerId = customerId, Amount = 10_000m },
            CancellationToken.None);
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);

        Result<FactoryResetResponse> result = await ResetAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.RequiresSetupWizard);

        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);
        FactoryResetCounts counts = await connection.QuerySingleAsync<FactoryResetCounts>(
            """
            SELECT
                (SELECT COUNT(*) FROM dbo.Customer) AS Customers,
                (SELECT COUNT(*) FROM dbo.ApiKey) AS ApiKeys,
                (SELECT COUNT(*) FROM dbo.CustomerBalance) AS CustomerBalances,
                (SELECT COUNT(*) FROM dbo.SenderLine) AS SenderLines,
                (SELECT COUNT(*) FROM dbo.ProviderAccount) AS ProviderAccounts,
                (SELECT COUNT(*) FROM dbo.Tariff) AS Tariffs,
                (SELECT COUNT(*) FROM dbo.Provider) AS Providers,
                (SELECT COUNT(*) FROM dbo.MessageType) AS MessageTypes;
            """);

        Assert.Equal(0, counts.Customers);
        Assert.Equal(0, counts.ApiKeys);
        Assert.Equal(0, counts.CustomerBalances);
        Assert.Equal(0, counts.SenderLines);
        Assert.Equal(0, counts.ProviderAccounts);
        Assert.Equal(0, counts.Tariffs);
        Assert.Equal(0, counts.Providers);
        Assert.Equal(0, counts.MessageTypes);

        Result<CreateCustomerResponse> firstCustomer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = "First after reset", Code = "first-after-reset" },
            CancellationToken.None);
        Assert.True(firstCustomer.IsSuccess);
        Assert.Equal((short)1, firstCustomer.Value.Id);
    }

    [Fact]
    public async Task Permanently_rejects_reset_when_an_sms_exists()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("has-message");
        await new TopUpHandler(_db).Handle(
            new TopUpRequest { CustomerId = customerId, Amount = 10_000m },
            CancellationToken.None);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                SenderLine = "30001234",
                MessageTypeId = 1,
                ClientBatchId = "factory-reset-guard",
                Messages = [new SendMessageItem { Recipient = "989120000001", Text = "Do not reset" }],
            },
            identity,
            CancellationToken.None);
        Assert.True(send.IsSuccess, send.Error?.Message);

        Result<FactoryResetResponse> result = await ResetAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("setup.factory_reset_messages_exist", result.Error.Code);

        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);
        int messageCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message;");
        int customerCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Customer;");
        Assert.Equal(1, messageCount);
        Assert.Equal(1, customerCount);
    }

    private async Task<Result<FactoryResetResponse>> ResetAsync() =>
        await new FactoryResetHandler(_db, TimeProvider.System).Handle(
            new FactoryResetRequest { Confirmation = FactoryResetRequest.RequiredConfirmation },
            CancellationToken.None);

    private async Task<short> CreateCustomerAsync(string code)
    {
        Result<CreateCustomerResponse> result = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest { Name = code, Code = code },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.Id;
    }

    private async Task<ApiKeyIdentity> IssueApiKeyAsync(short customerId)
    {
        Result<IssueApiKeyResponse> result = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = customerId, Name = "factory-reset-test" },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return new ApiKeyIdentity(result.Value.Id, customerId, result.Value.KeyPrefix);
    }

    private sealed record FactoryResetCounts(
        int Customers,
        int ApiKeys,
        int CustomerBalances,
        int SenderLines,
        int ProviderAccounts,
        int Tariffs,
        int Providers,
        int MessageTypes);
}
