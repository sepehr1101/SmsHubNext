using DbUp.Engine;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Time;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
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

namespace SmsHubNext.IntegrationTests.Features.Batches;

public sealed class BatchesTests : IAsyncLifetime
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

        Result<IReadOnlyList<BatchEvent>> events = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        BatchEvent accepted = Assert.Single(events.Value);
        Assert.Equal(MessageBatchEventType.Accepted, accepted.EventType);
        Assert.Equal(BatchStatus.Received, accepted.BatchStatus);
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

        Result<IReadOnlyList<BatchEvent>> events = await new ListBatchEventsHandler(_db).Handle(999999, CancellationToken.None);
        Assert.True(events.IsFailure);
        Assert.Equal(ErrorType.NotFound, events.Error!.Type);
    }

    [Fact]
    public async Task Browse_filters_exact_received_time_and_pages_tied_timestamps_without_duplicates()
    {
        CancellationToken token = CancellationToken.None;
        long firstId = await SendBatchAsync();
        Batch original = (await new GetBatchHandler(_db).Handle(firstId, token)).Value;
        ApiKeyIdentity identity = new(original.ApiKeyId, original.CustomerId, "test");
        List<long> ids = [firstId];
        for (int index = 0; index < 27; index++)
        {
            Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
                new SendMessagesRequest { SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = $"browse-{index}",
                    Messages = [new SendMessageItem { Recipient = "989120000001", Text = "Browse" }] }, identity, token);
            Assert.True(send.IsSuccess, send.Error?.Message);
            ids.Add(send.Value.BatchId);
        }
        DateTime from = new(2026, 10, 17, 20, 30, 0, DateTimeKind.Utc);
        DateTime end = from.AddMinutes(1);
        await using SqlConnection connection = await _db.OpenConnectionAsync(token);
        await connection.ExecuteAsync("UPDATE dbo.MessageBatch SET ReceivedAtUtc = @At, SubmitDateJalali = @Date",
            new { At = from, Date = JalaliDate.FromUtc(from) });
        await connection.ExecuteAsync("UPDATE dbo.MessageBatch SET ReceivedAtUtc = @At, SubmitDateJalali = @Date WHERE Id = @Id",
            new { At = from.AddMilliseconds(-1), Date = JalaliDate.FromUtc(from.AddMilliseconds(-1)), Id = firstId });
        await connection.ExecuteAsync("UPDATE dbo.MessageBatch SET ReceivedAtUtc = @At WHERE Id = @Id", new { At = end, Id = ids[^1] });

        BrowseBatchesHandler handler = new(_db);
        BrowseBatchesRequest query = new(from, end);
        Result<BatchListPage> first = await handler.Handle(query, token);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(25, first.Value.Items.Count);
        Assert.NotNull(first.Value.Next);
        BatchListPage second = (await handler.Handle(query with { Before = first.Value.Next }, token)).Value;
        Assert.Single(second.Items);
        Assert.Null(second.Next);
        long[] found = first.Value.Items.Concat(second.Items).Select(item => item.Id).ToArray();
        Assert.Equal(26, found.Distinct().Count());
        Assert.DoesNotContain(firstId, found);
        Assert.DoesNotContain(ids[^1], found);
    }

    [Fact]
    public async Task Selected_batch_messages_are_loaded_in_bounded_pages()
    {
        long batchId = await SendBatchAsync(27);
        ListBatchMessagesHandler handler = new(_db);
        BatchMessagesPage first = (await handler.HandlePage(batchId, null, CancellationToken.None)).Value;
        Assert.Equal(25, first.Items.Count);
        Assert.NotNull(first.NextAfterId);
        BatchMessagesPage second = (await handler.HandlePage(batchId, first.NextAfterId, CancellationToken.None)).Value;
        Assert.Equal(2, second.Items.Count);
        Assert.Null(second.NextAfterId);
        Assert.Equal(27, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
    }

    private async Task<long> SendBatchAsync(int messageCount = 2)
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "batch-reader", Code = "batch-reader" }, CancellationToken.None);
        short customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = $"batches-{Guid.NewGuid():N}",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = Enumerable.Range(0, messageCount).Select(index => new SendMessageItem
                    { Recipient = $"98912000{index:0000}", Text = "Hello" }).ToList(),
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return send.Value.BatchId;
    }
}
