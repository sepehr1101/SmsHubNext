using DbUp.Engine;
using Dapper;
using Microsoft.Data.SqlClient;
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
    public async Task Live_monitor_uses_real_states_and_event_time_and_reuses_snapshot_for_viewers()
    {
        long batchId = await SendBatchAsync();
        DateTimeOffset now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        DateTime end = LiveDispatchBuckets.Floor(now.UtcDateTime);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        long id = await connection.ExecuteScalarAsync<long>("SELECT MIN(Id) FROM dbo.Message WHERE MessageBatchId = @Id", new { Id = batchId });
        await connection.ExecuteAsync("""
            UPDATE dbo.Message SET Status = CASE WHEN Id = @Id THEN 2 ELSE 4 END WHERE MessageBatchId = @Batch;
            INSERT dbo.DispatchAcceptance(Id,AcceptedAtUtc,ProviderId)
            SELECT Id,@At,ProviderId FROM dbo.Message WHERE Id = @Id;
            """, new { Id = id, Batch = batchId, At = end.AddSeconds(-10) });
        LiveDispatchCache cache = new();
        LiveDispatchHandler handler = new(_db, new FixedClock(now), cache);
        Result<LiveSnapshot> result = await handler.Summary(CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Totals.Accepted);
        Assert.Equal(1, result.Value.Totals.Rejected);
        Assert.Equal(0, result.Value.Totals.Delivered);
        Assert.Equal(1, result.Value.Buckets[^1].Count);
        Assert.Equal(1, result.Value.Buckets.Sum(bucket => bucket.Count));
        Assert.Same(result.Value, (await handler.Summary(CancellationToken.None)).Value);
        Result<LiveMessagePage> failed = await handler.Messages(
            new LiveMessageRequest { Status = (byte)SendStatus.Rejected, Batch = batchId }, CancellationToken.None);
        Assert.True(failed.IsSuccess);
        Assert.Equal(SendStatus.Rejected, Assert.Single(failed.Value.Items).Status);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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

    [Fact]
    public async Task Live_monitor_searches_the_entire_window_and_pages_without_duplicates_or_offsets()
    {
        long batchId = await SendBatchAsync(80);
        LiveDispatchHandler handler = new(_db, TimeProvider.System, new LiveDispatchCache());
        LiveMessageRequest request = new() { Batch = batchId, PageSize = 25 };
        HashSet<long> seen = [];
        LiveMessage? oldest = null;
        int pageCount = 0;
        while (true)
        {
            Result<LiveMessagePage> result = await handler.Messages(request, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
            LiveMessagePage page = result.Value;
            Assert.InRange(page.Items.Count, 1, 25);
            Assert.All(page.Items, message => Assert.True(seen.Add(message.Id), "A message appeared on more than one page."));
            Assert.Equal(page.Items.OrderByDescending(message => message.SubmitDateJalali).ThenByDescending(message => message.Id), page.Items);
            oldest = page.Items[^1];
            pageCount++;
            if (!page.HasMore) { Assert.Null(page.NextCursor); break; }
            Assert.NotNull(page.NextCursor);
            request = request with { UntilUtc = page.UntilUtc, Before = page.NextCursor };
        }
        Assert.Equal(80, seen.Count);
        Assert.Equal(4, pageCount);
        Assert.NotNull(oldest);

        // The oldest message was not on the first page. Both national and stored
        // formats must search all matching rows, not just the currently loaded page.
        foreach (string search in new[] { "0" + oldest.MobileNumber[2..], oldest.MobileNumber, oldest.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            Result<LiveMessagePage> result = await handler.Messages(
                new LiveMessageRequest { Batch = batchId, Search = search }, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(oldest.Id, Assert.Single(result.Value.Items).Id);
            Assert.False(result.Value.HasMore);
        }

        Result<LiveMessagePage> filtered = await handler.Messages(
            new LiveMessageRequest { Batch = batchId, Search = oldest.MobileNumber, Status = (byte)SendStatus.Rejected }, CancellationToken.None);
        Assert.True(filtered.IsSuccess);
        Assert.Empty(filtered.Value.Items);
        Result<LiveMessagePage> largePage = await handler.Messages(
            new LiveMessageRequest { Batch = batchId, PageSize = 100 }, CancellationToken.None);
        Assert.Equal(80, largePage.Value.Items.Count);
        Assert.False(largePage.Value.HasMore);
    }

    [Fact]
    public async Task Batch_search_finds_records_outside_the_current_page_and_keeps_summary_filters_consistent()
    {
        long target = await SendBatchAsync();
        await SendBatchAsync();
        await SendBatchAsync();
        DispatchOperationsHandler handler = new(_db, TimeProvider.System);
        Result<DispatchOperationsBatchPage> first = await handler.Batches(new() { Take = 1 }, CancellationToken.None);
        Assert.DoesNotContain(first.Value.Items, item => item.Id == target);

        string persianId = SmsHubNext.Shared.Ui.PersianDisplay.Number(target);
        DispatchOperationsRequest search = new() { Search = persianId, Take = 1 };
        Result<DispatchOperationsBatchPage> filtered = await handler.Batches(search, CancellationToken.None);
        Assert.Equal(target, Assert.Single(filtered.Value.Items).Id);
        Assert.Equal(1, filtered.Value.TotalCount);
        Result<DispatchOperationsSummary> summary = await handler.Summary(search, CancellationToken.None);
        Assert.Equal(1, summary.Value.Totals.BatchCount);
        Assert.Equal(2, summary.Value.Totals.MessageCount);

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        string? clientId = await connection.ExecuteScalarAsync<string>(
            "SELECT ClientBatchId FROM dbo.MessageBatch WHERE Id = @target", new { target });
        Assert.NotNull(clientId);
        Result<DispatchOperationsBatchPage> byReference = await handler.Batches(new() { Search = clientId }, CancellationToken.None);
        Assert.Equal(target, Assert.Single(byReference.Value.Items).Id);
        Result<DispatchOperationsBatchPage> problems = await handler.Batches(new() { Search = persianId, OnlyProblems = true }, CancellationToken.None);
        Assert.Empty(problems.Value.Items);
        Assert.Equal(0, problems.Value.TotalCount);
        Result<DispatchOperationsBatchPage> cleared = await handler.Batches(new() { Search = " " }, CancellationToken.None);
        Assert.Equal(3, cleared.Value.TotalCount);

        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        SmsHubNext.Features.Batches.BrowseBatchesHandler browser = new(_db);
        Result<SmsHubNext.Features.Batches.BatchListPage> legacy = await browser.Handle(
            new(now.AddDays(-1), now.AddDays(1)) { Search = persianId }, CancellationToken.None);
        Assert.Equal(target, Assert.Single(legacy.Value.Items).Id);
    }

    [Fact]
    public async Task Batch_search_treats_sql_wildcards_as_literal_text()
    {
        await SendBatchAsync();
        DispatchOperationsHandler handler = new(_db, TimeProvider.System);
        Result<DispatchOperationsBatchPage> result = await handler.Batches(new() { Search = "%" }, CancellationToken.None);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    private async Task<long> SendBatchAsync(int count = 2)
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
                Messages = Enumerable.Range(1, count)
                    .Select(index => new SendMessageItem { Recipient = $"98912{index:D7}", Text = "Hello" }).ToList(),
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return send.Value.BatchId;
    }
}
