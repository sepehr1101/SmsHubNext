using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DeliveryReports;
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

namespace SmsHubNext.IntegrationTests.Features.DeliveryReports;

public sealed class DeliveryReportsTests : IAsyncLifetime
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
    public async Task Ingesting_a_delivered_report_updates_the_read_model_and_appends_history()
    {
        long messageId = await SendOneAndGetMessageIdAsync();

        Result<IngestDeliveryReportResponse> ingest = await new IngestDeliveryReportHandler(_db, TimeProvider.System).Handle(
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
        BatchMessage message = await SingleMessageAsync(messageId);
        Assert.Equal(DeliveryStatus.Delivered, message.DeliveryStatus);

        // The report is recorded in the append-only history.
        Result<IReadOnlyList<DeliveryReport>> history = await new ListDeliveryReportsHandler(_db).Handle(messageId, CancellationToken.None);
        Assert.True(history.IsSuccess);
        Assert.Single(history.Value);
        Assert.Equal(DeliveryReportStatus.Delivered, history.Value[0].NormalizedStatus);
    }

    [Fact]
    public async Task A_rejected_report_maps_the_read_model_to_undelivered()
    {
        long messageId = await SendOneAndGetMessageIdAsync();

        Result<IngestDeliveryReportResponse> ingest = await new IngestDeliveryReportHandler(_db, TimeProvider.System).Handle(
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
    public async Task The_first_terminal_report_wins_in_the_read_model_and_history_keeps_both()
    {
        long messageId = await SendOneAndGetMessageIdAsync();
        IngestDeliveryReportHandler handler = new IngestDeliveryReportHandler(_db, TimeProvider.System);

        await handler.Handle(
            new IngestDeliveryReportRequest { MessageId = messageId, Status = DeliveryReportStatus.Undelivered, RawStatusCode = 10 },
            CancellationToken.None);
        Result<IngestDeliveryReportResponse> second = await handler.Handle(
            new IngestDeliveryReportRequest { MessageId = messageId, Status = DeliveryReportStatus.Delivered, RawStatusCode = 1 },
            CancellationToken.None);

        Assert.Equal(DeliveryStatus.Undelivered, second.Value.DeliveryStatus);
        Assert.False(second.Value.AppliedToReadModel);

        BatchMessage message = await SingleMessageAsync(messageId);
        Assert.Equal(DeliveryStatus.Undelivered, message.DeliveryStatus);

        Result<IReadOnlyList<DeliveryReport>> history = await new ListDeliveryReportsHandler(_db).Handle(messageId, CancellationToken.None);
        Assert.Equal(2, history.Value.Count);
    }

    [Fact]
    public async Task Ingesting_for_an_unknown_message_is_not_found()
    {
        Result<IngestDeliveryReportResponse> result = await new IngestDeliveryReportHandler(_db, TimeProvider.System).Handle(
            new IngestDeliveryReportRequest { MessageId = 999999, Status = DeliveryReportStatus.Delivered, RawStatusCode = 1 },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    private async Task<BatchMessage> SingleMessageAsync(long messageId)
    {
        // Read the message back through the batch projection (it carries DeliveryStatus).
        long batchId = await BatchIdForMessageAsync(messageId);
        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        return messages.Value.Single(m => m.Id == messageId);
    }

    private async Task<long> BatchIdForMessageAsync(long messageId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT MessageBatchId FROM dbo.Message WHERE Id = @Id;", new { Id = messageId });
    }

    private async Task<long> SendOneAndGetMessageIdAsync()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "dlr", Code = $"dlr-{Guid.NewGuid():N}" }, CancellationToken.None);
        short customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000001", Text = "Hello" }],
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(send.Value.BatchId, CancellationToken.None);
        return messages.Value.Single().Id;
    }
}
