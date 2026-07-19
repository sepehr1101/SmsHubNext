using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Sending;

public sealed class SendMessagesTests : IAsyncLifetime
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
    public async Task Persists_the_batch_debits_the_balance_and_queues_each_message()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("sender");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        // Two GSM-7 single-segment messages against the test-only 1000 IRR/segment tariff.
        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                ClientBatchId = "batch-1",
                Messages =
                [
                    new SendMessageItem { Recipient = "989120000001", Text = "Hello", BillId = "bill-1" },
                    new SendMessageItem { Recipient = "989120000002", Text = "World" },
                ],
            },
            identity,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.AcceptedCount);
        Assert.True(result.Value.BatchId > 0);

        await using SqlConnection connection = await _db.OpenConnectionAsync();

        // The header rolls up count/segments/cost and starts at Received (1).
        (int MessageCount, int SegmentCount, decimal TotalCost, byte Status) batch = await connection.QuerySingleAsync<(int MessageCount, int SegmentCount, decimal TotalCost, byte Status)>(
            "SELECT MessageCount, SegmentCount, TotalCost, Status FROM dbo.MessageBatch WHERE Id = @Id;",
            new { Id = result.Value.BatchId });
        Assert.Equal(2, batch.MessageCount);
        Assert.Equal(2, batch.SegmentCount);
        Assert.Equal(2000m, batch.TotalCost);
        Assert.Equal((byte)1, batch.Status); // BatchStatus.Received

        // Two messages, each Queued (1) / Pending (1), with the frozen cost snapshot.
        List<(string MobileNumber, byte Status, byte DeliveryStatus, decimal TotalCost, string SubmitDateJalali)> messages = (await connection.QueryAsync<(string MobileNumber, byte Status, byte DeliveryStatus, decimal TotalCost, string SubmitDateJalali)>(
            "SELECT MobileNumber, Status, DeliveryStatus, TotalCost, SubmitDateJalali FROM dbo.Message WHERE MessageBatchId = @Id ORDER BY Id;",
            new { Id = result.Value.BatchId })).ToList();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m =>
        {
            Assert.Equal((byte)1, m.Status);         // SendStatus.Queued
            Assert.Equal((byte)1, m.DeliveryStatus); // DeliveryStatus.Pending
            Assert.Equal(1000m, m.TotalCost);
            Assert.Equal(10, m.SubmitDateJalali.Length); // yyyy/MM/dd
        });

        // The bodies are stored in the satellite, keyed by message id.
        int bodyCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MessageBody b JOIN dbo.Message m ON m.Id = b.Id WHERE m.MessageBatchId = @Id;",
            new { Id = result.Value.BatchId });
        Assert.Equal(2, bodyCount);

        // The balance is debited and the ledger records the matching signed entry.
        Result<CustomerBalance> balance = await new GetBalanceHandler(_db, TimeProvider.System).Handle(customerId, CancellationToken.None);
        Assert.Equal(8000m, balance.Value.Balance);

        (byte Type, decimal Amount, long? MessageBatchId) debit = await connection.QuerySingleAsync<(byte Type, decimal Amount, long? MessageBatchId)>(
            "SELECT Type, Amount, MessageBatchId FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Type = 2;",
            new { CustomerId = customerId });
        Assert.Equal(-2000m, debit.Amount);
        Assert.Equal(result.Value.BatchId, debit.MessageBatchId);
    }

    [Fact]
    public async Task Repeating_the_same_client_batch_returns_the_original_without_a_second_debit()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("idempotent");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);
        SendMessagesRequest request = new SendMessagesRequest
        {
            ClientBatchId = "same-logical-request",
            SenderLine = "30001234",
            MessageTypeId = 1,
            Messages = [new SendMessageItem { Recipient = "989120000007", Text = "Hello" }],
        };
        SendMessagesHandler handler = new SendMessagesHandler(_db, TimeProvider.System);

        Result<SendMessagesResponse> first = await handler.Handle(request, identity, CancellationToken.None);
        Result<SendMessagesResponse> duplicate = await handler.Handle(request, identity, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.True(duplicate.IsSuccess, duplicate.Error?.Message);
        Assert.Equal(first.Value.BatchId, duplicate.Value.BatchId);
        Assert.True(duplicate.Value.IsDuplicate);

        SendMessagesRequest changedPayload = new SendMessagesRequest
        {
            ClientBatchId = request.ClientBatchId,
            SenderLine = request.SenderLine,
            MessageTypeId = request.MessageTypeId,
            Messages = [new SendMessageItem { Recipient = "989120000007", Text = "Different" }],
        };
        Result<SendMessagesResponse> mismatch = await handler.Handle(changedPayload, identity, CancellationToken.None);
        Assert.True(mismatch.IsFailure);
        Assert.Equal("sending.client_batch_payload_mismatch", mismatch.Error!.Code);

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int batchCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MessageBatch WHERE CustomerId = @CustomerId;",
            new { CustomerId = customerId });
        Assert.Equal(1, batchCount);
        Assert.Equal(9000m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId;",
            new { CustomerId = customerId }));
    }

    [Fact]
    public async Task Concurrent_retries_of_the_same_client_batch_create_one_batch_and_one_debit()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("concurrent-idempotent");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);
        SendMessagesRequest request = new SendMessagesRequest
        {
            ClientBatchId = "concurrent-same-request",
            SenderLine = "30001234",
            MessageTypeId = 1,
            Messages = [new SendMessageItem { Recipient = "989120000008", Text = "Hello" }],
        };

        Task<Result<SendMessagesResponse>>[] tasks = Enumerable.Range(0, 8)
            .Select(_ => new SendMessagesHandler(_db, TimeProvider.System)
                .Handle(request, identity, CancellationToken.None))
            .ToArray();
        Result<SendMessagesResponse>[] results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Assert.Single(results.Select(result => result.Value.BatchId).Distinct());

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MessageBatch WHERE CustomerId = @CustomerId;",
            new { CustomerId = customerId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Type = 2;",
            new { CustomerId = customerId }));
        Assert.Equal(9000m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId;",
            new { CustomerId = customerId }));
    }

    [Fact]
    public async Task Rejects_the_batch_when_the_balance_is_insufficient()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("broke");
        await TopUpAsync(customerId, 500m); // less than the 1000 IRR a single segment costs
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = "insufficient-balance",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000003", Text = "Hello" }],
            },
            identity,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("sending.insufficient_balance", result.Error.Code);

        // Nothing was persisted and the balance is untouched (the transaction rolled back).
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int messageCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Message WHERE CustomerId = @CustomerId;", new { CustomerId = customerId });
        Assert.Equal(0, messageCount);

        Result<CustomerBalance> balance = await new GetBalanceHandler(_db, TimeProvider.System).Handle(customerId, CancellationToken.None);
        Assert.Equal(500m, balance.Value.Balance);
    }

    [Fact]
    public async Task Rejects_an_unknown_sender_line()
    {
        short customerId = await CreateCustomerAsync("liner");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = "unknown-sender-line",
                CustomerId = customerId,
                SenderLine = "99999999",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000004", Text = "Hello" }],
            },
            identity,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Rejects_sender_line_without_provider_account_credentials()
    {
        await ProviderAccountTestData.EnsureSenderLineAsync(_db, "30001234");
        short customerId = await CreateCustomerAsync("no-creds");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = "missing-provider-account",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000005", Text = "Hello" }],
            },
            identity,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("sending.provider_credentials_required", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_sender_line_with_inactive_provider_account()
    {
        await ProviderAccountTestData.EnsureSenderLineAsync(_db, "30001234");
        int providerAccountId = await ProviderAccountTestData.CreateMagfaAccountAsync(_db, isActive: false);
        await ProviderAccountTestData.AssignSenderLineAsync(_db, "30001234", providerAccountId);
        short customerId = await CreateCustomerAsync("inactive-creds");
        await TopUpAsync(customerId, 10000m);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);

        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = "inactive-provider-account",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000006", Text = "Hello" }],
            },
            identity,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("sending.provider_account_inactive", result.Error.Code);
    }

    private async Task<short> CreateCustomerAsync(string code)
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = code, Code = code }, CancellationToken.None);
        Assert.True(customer.IsSuccess);
        return customer.Value.Id;
    }

    private async Task TopUpAsync(short customerId, decimal amount)
    {
        Result<TopUpResponse> topUp = await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = amount }, CancellationToken.None);
        Assert.True(topUp.IsSuccess);
    }

    private async Task<ApiKeyIdentity> IssueApiKeyAsync(short customerId)
    {
        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "test-key" }, CancellationToken.None);
        Assert.True(key.IsSuccess);
        return new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix);
    }
}
