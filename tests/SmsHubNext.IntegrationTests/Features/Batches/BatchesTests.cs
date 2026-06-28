using DbUp.Engine;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Batches;

public sealed class BatchesTests : IAsyncLifetime
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
    public async Task Returns_the_batch_header_and_its_messages()
    {
        long batchId = await SendBatchAsync();

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(batchId, batch.Value.Id);
        Assert.Equal(BatchStatus.Received, batch.Value.Status);
        Assert.Equal(2, batch.Value.MessageCount);
        Assert.Equal(2000m, batch.Value.TotalCost);
        Assert.Null(batch.Value.StatusReason);
        Assert.Null(batch.Value.FinishedAtUtc);

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.True(messages.IsSuccess);
        Assert.Equal(2, messages.Value.Count);
        Assert.All(messages.Value, m =>
        {
            Assert.Equal(SendStatus.Queued, m.Status);
            Assert.Equal(DeliveryStatus.Pending, m.DeliveryStatus);
        });
    }

    [Fact]
    public async Task Getting_an_unknown_batch_is_not_found()
    {
        Result<Batch> batch = await new GetBatchHandler(_db).Handle(999999, CancellationToken.None);
        Assert.True(batch.IsFailure);
        Assert.Equal(ErrorType.NotFound, batch.Error!.Type);

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(999999, CancellationToken.None);
        Assert.True(messages.IsFailure);
        Assert.Equal(ErrorType.NotFound, messages.Error!.Type);
    }

    private async Task<long> SendBatchAsync()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "batch-reader", Code = "batch-reader" }, CancellationToken.None);
        short customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db).Handle(
            new SendMessagesRequest
            {
                CustomerId = customerId,
                ApiKeyId = key.Value.Id,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages =
                [
                    new SendMessageItem { Recipient = "989120000001", Text = "Hello" },
                    new SendMessageItem { Recipient = "989120000002", Text = "World" },
                ],
            },
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return send.Value.BatchId;
    }
}
