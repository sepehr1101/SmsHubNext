using Dapper;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.DeliveryReports;

public sealed class DeliveryReportsTests : IAsyncLifetime
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
    public async Task Ingesting_a_delivered_report_updates_the_read_model_and_appends_history()
    {
        var messageId = await SendOneAndGetMessageIdAsync();

        var ingest = await new IngestDeliveryReportHandler(_db).Handle(
            new IngestDeliveryReportRequest
            {
                MessageId = messageId,
                Status = DeliveryReportStatus.Delivered,
                RawStatusCode = 1,
            },
            CancellationToken.None);

        Assert.True(ingest.IsSuccess, ingest.Error?.Message);
        Assert.True(ingest.Value.ReportId > 0);
        Assert.Equal(DeliveryStatus.Delivered, ingest.Value.DeliveryStatus);

        // The message read model now reflects Delivered.
        var message = await SingleMessageAsync(messageId);
        Assert.Equal(DeliveryStatus.Delivered, message.DeliveryStatus);

        // The report is recorded in the append-only history.
        var history = await new ListDeliveryReportsHandler(_db).Handle(messageId, CancellationToken.None);
        Assert.True(history.IsSuccess);
        Assert.Single(history.Value);
        Assert.Equal(DeliveryReportStatus.Delivered, history.Value[0].NormalizedStatus);
    }

    [Fact]
    public async Task A_rejected_report_maps_the_read_model_to_undelivered()
    {
        var messageId = await SendOneAndGetMessageIdAsync();

        var ingest = await new IngestDeliveryReportHandler(_db).Handle(
            new IngestDeliveryReportRequest
            {
                MessageId = messageId,
                Status = DeliveryReportStatus.Rejected,
                RawStatusCode = 42,
            },
            CancellationToken.None);

        Assert.True(ingest.IsSuccess, ingest.Error?.Message);
        Assert.Equal(DeliveryStatus.Undelivered, ingest.Value.DeliveryStatus);
    }

    [Fact]
    public async Task The_latest_report_wins_in_the_read_model_and_history_keeps_both()
    {
        var messageId = await SendOneAndGetMessageIdAsync();
        var handler = new IngestDeliveryReportHandler(_db);

        await handler.Handle(
            new IngestDeliveryReportRequest { MessageId = messageId, Status = DeliveryReportStatus.Undelivered, RawStatusCode = 10 },
            CancellationToken.None);
        var second = await handler.Handle(
            new IngestDeliveryReportRequest { MessageId = messageId, Status = DeliveryReportStatus.Delivered, RawStatusCode = 1 },
            CancellationToken.None);

        Assert.Equal(DeliveryStatus.Delivered, second.Value.DeliveryStatus);

        var message = await SingleMessageAsync(messageId);
        Assert.Equal(DeliveryStatus.Delivered, message.DeliveryStatus);

        var history = await new ListDeliveryReportsHandler(_db).Handle(messageId, CancellationToken.None);
        Assert.Equal(2, history.Value.Count);
    }

    [Fact]
    public async Task Ingesting_for_an_unknown_message_is_not_found()
    {
        var result = await new IngestDeliveryReportHandler(_db).Handle(
            new IngestDeliveryReportRequest { MessageId = 999999, Status = DeliveryReportStatus.Delivered, RawStatusCode = 1 },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    private async Task<BatchMessage> SingleMessageAsync(long messageId)
    {
        // Read the message back through the batch projection (it carries DeliveryStatus).
        var batchId = await BatchIdForMessageAsync(messageId);
        var messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        return messages.Value.Single(m => m.Id == messageId);
    }

    private async Task<long> BatchIdForMessageAsync(long messageId)
    {
        await using var connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT MessageBatchId FROM dbo.Message WHERE Id = @Id;", new { Id = messageId });
    }

    private async Task<long> SendOneAndGetMessageIdAsync()
    {
        var customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "dlr", Code = $"dlr-{Guid.NewGuid():N}" }, CancellationToken.None);
        var customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        var key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        var send = await new SendMessagesHandler(_db).Handle(
            new SendMessagesRequest
            {
                CustomerId = customerId,
                ApiKeyId = key.Value.Id,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000001", Text = "Hello" }],
            },
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);

        var messages = await new ListBatchMessagesHandler(_db).Handle(send.Value.BatchId, CancellationToken.None);
        return messages.Value.Single().Id;
    }
}
