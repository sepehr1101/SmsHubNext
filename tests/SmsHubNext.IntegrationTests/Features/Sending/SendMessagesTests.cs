using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SmsHubNext.Shared.Http;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Reports;
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
    public async Task Resend_concurrent_requests_create_one_copy_and_one_additional_debit_and_keep_source_unchanged()
    {
        (long originalId, ApiKeyIdentity identity) = await CreateResendSource();
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status = 2, DeliveryStatus = 3 WHERE Id = @Id", new { Id = originalId });
        ResendMessageHandler handler = new(_db, new SendMessagesHandler(_db, TimeProvider.System));
        ResendMessageRequest request = new() { RequestId = Guid.NewGuid() };
        Result<ResendMessageResponse>[] results = await Task.WhenAll(
            handler.Handle(originalId, request, identity, CancellationToken.None),
            handler.Handle(originalId, request, identity, CancellationToken.None));
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Assert.Equal(results[0].Value.MessageId, results[1].Value.MessageId);
        Assert.Single(results, result => !result.Value.IsDuplicate);
        Assert.Single(results, result => result.Value.IsDuplicate);
        long newId = results[0].Value.MessageId;
        Assert.NotEqual(originalId, newId);
        (string Body, string MobileNumber, string BillId, string PayId, byte Status, decimal TotalCost) copy =
            await connection.QuerySingleAsync<(string, string, string, string, byte, decimal)>("""
                SELECT b.Body,m.MobileNumber,m.BillId,m.PayId,m.Status,m.TotalCost
                FROM dbo.Message m JOIN dbo.MessageBody b ON b.Id=m.Id WHERE m.Id=@Id
                """, new { Id = newId });
        Assert.Equal("Resend original", copy.Body);
        Assert.Equal("989120000001", copy.MobileNumber);
        Assert.Equal("bill-resend", copy.BillId);
        Assert.Equal("pay-resend", copy.PayId);
        Assert.Equal((byte)1, copy.Status);
        Assert.Equal(1000m, copy.TotalCost);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId=@CustomerId AND Type=@Type", new { identity.CustomerId, Type = (byte)SmsHubNext.Shared.Enums.BalanceTransactionType.Debit }));
        Assert.Equal(-2000m, await connection.ExecuteScalarAsync<decimal>("SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId=@CustomerId", new { identity.CustomerId }));
        Assert.Equal((byte)3, await connection.ExecuteScalarAsync<byte>("SELECT DeliveryStatus FROM dbo.Message WHERE Id=@Id", new { Id = originalId }));

        // A lost response can be replayed even after source-body retention or a changed status.
        await connection.ExecuteAsync("DELETE FROM dbo.MessageBody WHERE Id=@Id; UPDATE dbo.Message SET Status=5 WHERE Id=@Id", new { Id = originalId });
        Result<ResendMessageResponse> duplicate = await handler.Handle(originalId, request, identity, CancellationToken.None);
        Assert.True(duplicate.IsSuccess, duplicate.Error?.Message);
        Assert.True(duplicate.Value.IsDuplicate);
        Assert.Equal(newId, duplicate.Value.MessageId);
    }

    [Fact]
    public async Task Resend_rejects_other_tenants_uncertain_messages_and_expired_text_without_charging()
    {
        (long id, ApiKeyIdentity owner) = await CreateResendSource();
        ApiKeyIdentity other = await IssueApiKeyAsync(await CreateCustomerAsync("resend-other"));
        ResendMessageHandler handler = new(_db, new SendMessagesHandler(_db, TimeProvider.System));
        ResendMessageRequest request = new() { RequestId = Guid.NewGuid() };
        Result<ResendMessageResponse> forbidden = await handler.Handle(id, request, other, CancellationToken.None);
        Assert.Equal("sending.message_not_found", forbidden.Error?.Code);
        Assert.Equal("sending.resend_not_allowed", (await handler.Handle(id, request, owner, CancellationToken.None)).Error?.Code);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=6 WHERE Id=@Id", new { Id = id });
        Assert.Equal("sending.resend_not_allowed", (await handler.Handle(id, request, owner, CancellationToken.None)).Error?.Code);
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=2 WHERE Id=@Id", new { Id = id });
        Assert.Equal("sending.resend_not_allowed", (await handler.Handle(id, request, owner, CancellationToken.None)).Error?.Code);
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE Id=@Id; DELETE dbo.MessageBody WHERE Id=@Id", new { Id = id });
        Assert.Equal("sending.resend_body_missing", (await handler.Handle(id, request, owner, CancellationToken.None)).Error?.Code);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MessageBatch WHERE CustomerId=@CustomerId", new { owner.CustomerId }));
    }

    [Fact]
    public async Task Administrator_resend_bills_original_customer_and_records_real_administrator_key()
    {
        (long id, ApiKeyIdentity owner) = await CreateResendSource();
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE Id=@Id", new { Id = id });
        SmsHubNext.Deployment.SetupAdministratorCredential credential = await new SmsHubNext.Deployment.AdminApiKeyBootstrapper(_db)
            .CreateAsync("resend-admin", null, CancellationToken.None);
        Result<ApiKeyIdentity> admin = await new ApiKeyAuthenticator(_db, TimeProvider.System).Authenticate(credential.ApiKey, System.Net.IPAddress.Loopback, CancellationToken.None);
        Assert.True(admin.IsSuccess);
        ResendMessageHandler handler = new(_db, new SendMessagesHandler(_db, TimeProvider.System));
        Result<ResendMessageResponse> result = await handler.Handle(id, new() { RequestId = Guid.NewGuid() }, admin.Value, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        (short CustomerId, int ApiKeyId) batch = await connection.QuerySingleAsync<(short, int)>(
            "SELECT CustomerId,ApiKeyId FROM dbo.MessageBatch WHERE Id=@Id", new { Id = result.Value.BatchId });
        Assert.Equal(owner.CustomerId, batch.CustomerId);
        Assert.Equal(credential.Id, batch.ApiKeyId);
    }

    private async Task<(long MessageId, ApiKeyIdentity Identity)> CreateResendSource()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customer = await CreateCustomerAsync("resend-owner");
        ApiKeyIdentity identity = await IssueApiKeyAsync(customer);
        Result<SendMessagesResponse> original = await new SendMessagesHandler(_db, TimeProvider.System).Handle(new()
        {
            ClientBatchId="resend-source", SenderLine="30001234", MessageTypeId=1,
            Messages=[new SendMessageItem { Recipient="989120000001",Text="Resend original",BillId="bill-resend",PayId="pay-resend" }],
        }, identity, CancellationToken.None);
        Assert.True(original.IsSuccess, original.Error?.Message);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        long id = await connection.ExecuteScalarAsync<long>("SELECT Id FROM dbo.Message WHERE MessageBatchId=@Id", new { Id=original.Value.BatchId });
        return (id, identity);
    }

    [Fact]
    public async Task Resend_api_authenticates_customer_key_and_returns_accepted_message_identifiers()
    {
        (long id, ApiKeyIdentity owner) = await CreateResendSource();
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=2,DeliveryStatus=2 WHERE Id=@Id", new { Id=id });
        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId=owner.CustomerId,Name="external-resend" }, CancellationToken.None);
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices=serviceProvider };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Headers[ApiKeyConstants.HeaderName] = key.Value.Key;
        ResendMessageController controller = new(new ApiKeyAuthenticator(_db,TimeProvider.System),new(_db,new(_db,TimeProvider.System)))
        { ControllerContext=new ControllerContext { HttpContext=context } };
        ObjectResult response = Assert.IsType<ObjectResult>(
            await controller.Resend(id,new() { RequestId=Guid.NewGuid() },CancellationToken.None));
        Assert.Equal(202,response.StatusCode);
        ApiResponse<ResendMessageResponse> envelope = Assert.IsType<ApiResponse<ResendMessageResponse>>(response.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(id,envelope.Data.OriginalMessageId);
        Assert.NotEqual(id,envelope.Data.MessageId);
    }

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

    [Theory]
    [InlineData(500)]
    [InlineData(0)]
    public async Task Accepts_the_batch_and_records_debt_when_balance_is_insufficient(int initialBalance)
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("broke");
        if (initialBalance > 0) await TopUpAsync(customerId, initialBalance);
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);
        SendMessagesRequest request = new()
        {
            ClientBatchId = "insufficient-balance", CustomerId = customerId, SenderLine = "30001234", MessageTypeId = 1,
            Messages = [new SendMessageItem { Recipient = "989120000003", Text = "Hello" }],
        };
        SendMessagesHandler handler = new(_db, TimeProvider.System);
        Result<SendMessagesResponse> result = await handler.Handle(request, identity, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Message WHERE CustomerId = @CustomerId;", new { CustomerId = customerId }));
        Assert.Equal(initialBalance - 1000m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT BalanceAfter FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Type = 2;", new { CustomerId = customerId }));
        Result<SendMessagesResponse> duplicate = await handler.Handle(request, identity, CancellationToken.None);
        Assert.True(duplicate.IsSuccess, duplicate.Error?.Message);
        Assert.True(duplicate.Value.IsDuplicate);
        Assert.Equal(initialBalance - 1000m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId = @CustomerId;", new { CustomerId = customerId }));
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

    [Fact]
    public async Task Accepted_messages_can_be_found_by_bill_payment_phone_and_date_with_keyset_paging()
    {
        CancellationToken token = CancellationToken.None;
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("message-search");
        ApiKeyIdentity identity = await IssueApiKeyAsync(customerId);
        List<SendMessageItem> messages = Enumerable.Range(0, 27).Select(index => new SendMessageItem
        {
            Recipient = $"98912000{index:0000}", Text = $"Search message {index}", BillId = "00123",
            PayId = index < 2 ? "98765" : "other-payment",
        }).ToList();
        Result<SendMessagesResponse> accepted = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest { SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = "search-batch", Messages = messages }, identity, token);
        Assert.True(accepted.IsSuccess, accepted.Error?.Message);
        await using SqlConnection connection = await _db.OpenConnectionAsync(token);
        string date = await connection.QuerySingleAsync<string>("SELECT SubmitDateJalali FROM dbo.MessageBatch WHERE Id = @Id", new { Id = accepted.Value.BatchId });
        SearchMessagesHandler handler = new(_db);
        SearchMessagesRequest query = new() { FromJalali = date, ToJalali = date, Field = MessageSearchField.BillId, Value = "۰۰۱۲۳" };

        Result<MessageSearchPage> first = await handler.Handle(query, token);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(25, first.Value.Items.Count);
        Assert.NotNull(first.Value.Next);
        Result<MessageSearchPage> second = await handler.Handle(query with { Before = first.Value.Next }, token);
        Assert.Equal(2, second.Value.Items.Count);
        Assert.Null(second.Value.Next);
        Assert.Equal(27, first.Value.Items.Concat(second.Value.Items).Select(item => item.Id).Distinct().Count());
        Assert.All(first.Value.Items, item => Assert.Equal("00123", item.BillId));
        Assert.All(first.Value.Items, item => Assert.NotNull(item.Body));

        Result<MessageSearchPage> payment = await handler.Handle(query with { Field = MessageSearchField.PayId, Value = "۹۸۷۶۵" }, token);
        Assert.Equal(2, payment.Value.Items.Count);
        Result<MessageSearchPage> phone = await handler.Handle(query with { Field = MessageSearchField.MobileNumber, Value = "۰۹۱۲" + new string('۰', 7) }, token);
        MessageSearchRow row = Assert.Single(phone.Value.Items);
        Assert.Equal("989120000000", row.MobileNumber);
        Assert.Equal("Search message 0", row.Body);
        Assert.Empty((await handler.Handle(query with { Value = "123" }, token)).Value.Items);
        Assert.Empty((await handler.Handle(query with { FromJalali = "1400/01/01", ToJalali = "1400/01/01" }, token)).Value.Items);

        await connection.ExecuteAsync("DELETE FROM dbo.MessageBody WHERE Id = @Id", new { row.Id });
        Result<MessageSearchPage> retained = await handler.Handle(query with { Field = MessageSearchField.MobileNumber, Value = "989120000000" }, token);
        Assert.Null(Assert.Single(retained.Value.Items).Body);
    }

    [Fact]
    public async Task Customer_message_search_never_returns_another_tenants_matching_bill_or_phone()
    {
        CancellationToken token = CancellationToken.None;
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short firstCustomer = await CreateCustomerAsync("search-tenant-a");
        short secondCustomer = await CreateCustomerAsync("search-tenant-b");
        ApiKeyIdentity firstKey = await IssueApiKeyAsync(firstCustomer);
        ApiKeyIdentity secondKey = await IssueApiKeyAsync(secondCustomer);
        SendMessagesRequest send = new()
        {
            SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = "shared-client-id",
            Messages = [new SendMessageItem { Recipient = "989120000001", BillId = "00123", Text = "Tenant-private message" }],
        };
        Result<SendMessagesResponse> firstBatch = await new SendMessagesHandler(_db, TimeProvider.System).Handle(send, firstKey, token);
        Result<SendMessagesResponse> secondBatch = await new SendMessagesHandler(_db, TimeProvider.System).Handle(send, secondKey, token);
        Assert.True(firstBatch.IsSuccess, firstBatch.Error?.Message);
        Assert.True(secondBatch.IsSuccess, secondBatch.Error?.Message);
        await using SqlConnection connection = await _db.OpenConnectionAsync(token);
        string date = await connection.QuerySingleAsync<string>("SELECT SubmitDateJalali FROM dbo.MessageBatch WHERE Id = @Id", new { Id = firstBatch.Value.BatchId });
        SearchMessagesHandler handler = new(_db);
        foreach (MessageSearchField field in new[] { MessageSearchField.BillId, MessageSearchField.MobileNumber })
        {
            SearchMessagesRequest query = new() { FromJalali = date, ToJalali = date, Field = field, Value = field == MessageSearchField.BillId ? "00123" : "09120000001" };
            Result<MessageSearchPage> first = await handler.HandleForCustomer(query, firstKey, token);
            Result<MessageSearchPage> second = await handler.HandleForCustomer(query, secondKey, token);
            Assert.Equal(firstBatch.Value.BatchId, Assert.Single(first.Value.Items).MessageBatchId);
            Assert.Equal(secondBatch.Value.BatchId, Assert.Single(second.Value.Items).MessageBatchId);
            MessageSearchPage next = (await handler.HandleForCustomer(query with { Before = new(date, long.MaxValue) }, firstKey, token)).Value;
            Assert.All(next.Items, item => Assert.Equal(firstBatch.Value.BatchId, item.MessageBatchId));
        }
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
