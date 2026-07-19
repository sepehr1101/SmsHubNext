using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DispatchOperations;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.GeoSections;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Features.ReferenceData.Providers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.DispatchOperations;

public sealed class DispatchOperationsTests : IAsyncLifetime
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
    public async Task Summary_and_batch_list_report_database_backed_dispatch_queue_state()
    {
        long batchId = await SendBatchAsync();
        DispatchOperationsHandler handler = new DispatchOperationsHandler(_db, TimeProvider.System);

        Result<DispatchOperationsSummary> summary =
            await handler.Summary(new DispatchOperationsRequest(), CancellationToken.None);

        Assert.True(summary.IsSuccess, summary.Error?.Message);
        Assert.Equal(1, summary.Value.Totals.BatchCount);
        Assert.Equal(2, summary.Value.Totals.MessageCount);
        Assert.Equal(1, summary.Value.Totals.DueBatchCount);
        Assert.Equal(0, summary.Value.Totals.AwaitingConfirmationBatchCount);
        Assert.Contains(summary.Value.BatchStatuses, row =>
            row.Status == BatchStatus.Received && row.BatchCount == 1 && row.MessageCount == 2);
        Assert.Contains(summary.Value.MessageStatuses, row =>
            row.Status == SendStatus.Queued && row.MessageCount == 2);

        Result<DispatchOperationsBatchPage> page =
            await handler.Batches(new DispatchOperationsRequest(), CancellationToken.None);

        Assert.True(page.IsSuccess, page.Error?.Message);
        DispatchOperationsBatchRow row = Assert.Single(page.Value.Items);
        Assert.Equal(batchId, row.Id);
        Assert.Equal(BatchStatus.Received, row.Status);
        Assert.Equal(2, row.QueuedMessageCount);
        Assert.Equal(MessageBatchEventType.Accepted, row.LastEventType);

        Result<DispatchOperationsBatchPage> problems =
            await handler.Batches(new DispatchOperationsRequest { OnlyProblems = true }, CancellationToken.None);

        Assert.True(problems.IsSuccess, problems.Error?.Message);
        Assert.Empty(problems.Value.Items);
    }

    private async Task<long> SendBatchAsync()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(
                new CreateCustomerRequest { Name = "dispatch-ops", Code = $"dispatch-ops-{Guid.NewGuid():N}" },
                CancellationToken.None);
        short customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = $"dispatch-operations-{Guid.NewGuid():N}",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages =
                [
                    new SendMessageItem { Recipient = "989120000001", Text = "Hello" },
                    new SendMessageItem { Recipient = "989120000002", Text = "World" },
                ],
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return send.Value.BatchId;
    }
}
