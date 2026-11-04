using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Deployment;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Time;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Sending;

public sealed partial class SendMessagesTests
{
    [Fact]
    public async Task Daily_resend_filters_unsafe_states_and_concurrent_replays_and_restart_never_duplicate_or_double_debit()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(12);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            UPDATE dbo.Message SET Status=4 WHERE Id IN @Rejected;
            UPDATE dbo.Message SET Status=2,DeliveryStatus=3,ProviderMessageId=CONCAT('provider-',Id) WHERE Id IN @Undelivered;
            UPDATE dbo.Message SET Status=2,DeliveryStatus=2,ProviderMessageId='delivered' WHERE Id=@Delivered;
            UPDATE dbo.Message SET Status=6 WHERE Id=@Uncertain;
            UPDATE dbo.Message SET Status=2,DeliveryStatus=4,ProviderMessageId='expired' WHERE Id=@Expired;
            UPDATE dbo.Message SET MobileNumber='989120000008' WHERE Id=@Duplicate;
            DELETE dbo.MessageBody WHERE Id=@MissingBody;
            INSERT dbo.DispatchAcceptance (Id,AcceptedAtUtc,ProviderId) SELECT Id,SYSUTCDATETIME(),ProviderId FROM dbo.Message WHERE Id=@Contradictory;
            """, new { Rejected = new[] { ids[0], ids[7], ids[8], ids[9], ids[10] },
                Undelivered = new[] { ids[1], ids[6], ids[11] }, Delivered = ids[2], Uncertain = ids[3], Expired = ids[5],
                Duplicate = ids[8], MissingBody = ids[9], Contradictory = ids[10] });
        await AddDailyReport(connection, ids[1], 2);
        await AddDailyReport(connection, ids[5], 3);
        await AddDailyReport(connection, ids[11], 1); // Current row claims failure, history proves delivery.
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Result<DailyResendSummary>[] beginnings = await Task.WhenAll(
            handler.Begin(day, admin, CancellationToken.None), handler.Begin(day, admin, CancellationToken.None));
        Assert.All(beginnings, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Guid operationId = beginnings[0].Value.Id;
        Assert.Equal(operationId, beginnings[1].Value.Id);
        Assert.True(await DailyProcessor().ProcessNext(CancellationToken.None));
        DailyResendSummary ready = (await handler.Status(operationId, admin, CancellationToken.None)).Value;
        Assert.Equal(DailyResendState.Ready, ready.State);
        Assert.Equal(2, ready.Eligible);
        Assert.Equal(2000m, ready.EstimatedCost);
        Assert.Equal(3, ready.Skipped);
        Assert.Equal(3, ready.SkipReasons.Sum(reason => reason.Count));
        Assert.Contains(ready.SkipReasons, reason => reason.Code == "sending.daily_ambiguous_recipient" && reason.Count == 2);
        Assert.Contains(ready.SkipReasons, reason => reason.Code == "sending.resend_body_missing" && reason.Count == 1);
        Assert.Equal(0, ready.Queued);
        await Task.WhenAll(handler.Confirm(operationId, admin, CancellationToken.None), handler.Confirm(operationId, admin, CancellationToken.None));
        await Task.WhenAll(DailyProcessor().ProcessNext(CancellationToken.None), DailyProcessor().ProcessNext(CancellationToken.None));
        DailyResendSummary completed = (await handler.Status(operationId, admin, CancellationToken.None)).Value;
        Assert.Equal(DailyResendState.Completed, completed.State);
        Assert.Equal(2, completed.Queued);
        Assert.Equal(2, completed.Pending);
        Assert.Equal(0, completed.Submitted);
        Assert.Equal(3, await DebitCount(connection, owner.CustomerId));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MessageBatch WHERE ApiKeyId=@Id", new { Id = admin.ApiKeyId }));
        Assert.Equal(operationId, (await handler.Begin(day, admin, CancellationToken.None)).Value.Id);

        // Crash after acceptance but before recording the result in the manifest. The stored UUID must recover the same child.
        await connection.ExecuteAsync("""
            UPDATE dbo.DailyResendItem SET State=0,NewMessageId=NULL,NewBatchId=NULL WHERE OperationId=@Id AND State=1;
            UPDATE dbo.DailyResendOperation SET State=2 WHERE Id=@Id;
            DELETE dbo.MessageBody WHERE Id IN @Sources;
            """, new { Id = operationId, Sources = new[] { ids[0], ids[1] } });
        Assert.True(await DailyProcessor().ProcessNext(CancellationToken.None));
        Assert.Equal(2, (await handler.Status(operationId, admin, CancellationToken.None)).Value.Queued);
        Assert.Equal(3, await DebitCount(connection, owner.CustomerId));
        Assert.Equal(14, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message"));
        Result<ResendMessageResponse> manual = await new ResendMessageHandler(_db, new(_db, TimeProvider.System))
            .Handle(ids[0], new() { RequestId = Guid.NewGuid() }, admin, CancellationToken.None);
        Assert.Equal("sending.daily_already_resent", manual.Error?.Code);
    }

    [Fact]
    public async Task Daily_resend_competing_with_manual_resend_creates_only_one_copy()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(1);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE Id=@Id", new { Id = ids[0] });
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Guid id = (await handler.Begin(day, admin, CancellationToken.None)).Value.Id;
        await DailyProcessor().ProcessNext(CancellationToken.None);
        await handler.Confirm(id, admin, CancellationToken.None);
        ResendMessageHandler manual = new(_db, new(_db, TimeProvider.System));
        await Task.WhenAll(manual.Handle(ids[0], new() { RequestId = Guid.NewGuid() }, admin, CancellationToken.None),
            DailyProcessor().ProcessNext(CancellationToken.None));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message"));
        Assert.Equal(2, await DebitCount(connection, owner.CustomerId));
    }

    [Fact]
    public async Task Daily_resend_rechecks_prices_and_status_and_pauses_if_confirmer_is_revoked()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(2);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE CustomerId=@CustomerId", new { owner.CustomerId });
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Guid id = (await handler.Begin(day, admin, CancellationToken.None)).Value.Id;
        await DailyProcessor().ProcessNext(CancellationToken.None);
        await handler.Confirm(id, admin, CancellationToken.None);
        await connection.ExecuteAsync("UPDATE dbo.ApiKey SET IsActive=0 WHERE Id=@Id", new { Id = admin.ApiKeyId });
        await DailyProcessor().ProcessNext(CancellationToken.None);
        Assert.Equal(DailyResendState.Paused, (await handler.Status(id, admin, CancellationToken.None)).Value.State);
        Assert.Equal(1, await DebitCount(connection, owner.CustomerId));
        await connection.ExecuteAsync("""
            UPDATE dbo.ApiKey SET IsActive=1 WHERE Id=@Id;
            UPDATE dbo.TariffRate SET PricePerSegment=PricePerSegment+500;
            UPDATE dbo.Message SET Status=6 WHERE Id=@Source;
            """, new { Id = admin.ApiKeyId, Source = ids[1] });
        await handler.Confirm(id, admin, CancellationToken.None);
        await DailyProcessor().ProcessNext(CancellationToken.None);
        DailyResendSummary result = (await handler.Status(id, admin, CancellationToken.None)).Value;
        Assert.Equal(DailyResendState.Completed, result.State);
        Assert.Equal(0, result.Queued);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(1, await DebitCount(connection, owner.CustomerId));
        Assert.Contains("sending.resend_price_changed", await connection.QueryAsync<string>("SELECT SkipCode FROM dbo.DailyResendItem WHERE OperationId=@Id", new { Id = id }));
    }

    private DailyResendProcessor DailyProcessor() => new(_db, new(_db, new(_db, TimeProvider.System)), TimeProvider.System);

    [Fact]
    public async Task Daily_resend_partial_run_exposes_only_unprocessed_count_and_cost_for_reapproval()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(28);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE CustomerId=@CustomerId", new { owner.CustomerId });
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Guid id = (await handler.Begin(day, admin, CancellationToken.None)).Value.Id;
        await DailyProcessor().ProcessNext(CancellationToken.None);
        DailyResendSummary ready = (await handler.Status(id, admin, CancellationToken.None)).Value;
        Assert.Equal(28, ready.Remaining);
        Assert.Equal(28000m, ready.RemainingCost);
        await handler.Confirm(id, admin, CancellationToken.None);
        await DailyProcessor().ProcessNext(CancellationToken.None);
        await connection.ExecuteAsync("UPDATE dbo.ApiKey SET IsActive=0 WHERE Id=@Id", new { Id = admin.ApiKeyId });
        await DailyProcessor().ProcessNext(CancellationToken.None);
        DailyResendSummary paused = (await handler.Status(id, admin, CancellationToken.None)).Value;
        Assert.Equal(DailyResendState.Paused, paused.State);
        Assert.Equal(25, paused.Queued);
        Assert.Equal(3, paused.Remaining);
        Assert.Equal(3000m, paused.RemainingCost);
        Assert.Equal(28000m, paused.EstimatedCost);
        Assert.Empty(paused.SkipReasons);
        Assert.Equal(26, await DebitCount(connection, owner.CustomerId));
    }

    [Fact]
    public async Task Daily_resend_new_same_recipient_message_or_manual_copy_after_review_is_skipped()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(2);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Message SET Status=4 WHERE CustomerId=@CustomerId", new { owner.CustomerId });
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Guid id = (await handler.Begin(day, admin, CancellationToken.None)).Value.Id;
        await DailyProcessor().ProcessNext(CancellationToken.None);
        Result<SendMessagesResponse> fresh = await new SendMessagesHandler(_db, TimeProvider.System).Handle(new()
        {
            ClientBatchId = "new-after-review", SenderLine = "30001234", MessageTypeId = 1,
            Messages = [new SendMessageItem { Recipient = "989120000001", Text = "New notification" }],
        }, owner, CancellationToken.None);
        Assert.True(fresh.IsSuccess, fresh.Error?.Message);
        Result<ResendMessageResponse> manual = await new ResendMessageHandler(_db, new(_db, TimeProvider.System))
            .Handle(ids[1], new() { RequestId = Guid.NewGuid() }, admin, CancellationToken.None);
        Assert.True(manual.IsSuccess, manual.Error?.Message);
        await handler.Confirm(id, admin, CancellationToken.None);
        await DailyProcessor().ProcessNext(CancellationToken.None);
        DailyResendSummary result = (await handler.Status(id, admin, CancellationToken.None)).Value;
        Assert.Equal(0, result.Queued);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message"));
        Assert.Equal(3, await DebitCount(connection, owner.CustomerId));
    }

    [Fact]
    public async Task Daily_resend_late_delivery_evidence_before_dispatch_holds_the_new_batch_without_provider_acceptance()
    {
        (long[] ids, ApiKeyIdentity owner, ApiKeyIdentity admin, string day) = await DailySources(1);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            UPDATE dbo.Message SET Status=2,DeliveryStatus=3,ProviderMessageId='provider-source' WHERE Id=@Id;
            UPDATE dbo.MessageBatch SET Status=3 WHERE CustomerId=@CustomerId;
            """, new { Id = ids[0], owner.CustomerId });
        await AddDailyReport(connection, ids[0], 2);
        DailyResendHandler handler = new(_db, TimeProvider.System);
        Guid id = (await handler.Begin(day, admin, CancellationToken.None)).Value.Id;
        await DailyProcessor().ProcessNext(CancellationToken.None);
        await handler.Confirm(id, admin, CancellationToken.None);
        await DailyProcessor().ProcessNext(CancellationToken.None);
        await AddDailyReport(connection, ids[0], 1);
        MessageDispatcher dispatcher = new(_db, new SmsProviderRegistry([new LoopbackSmsProvider()]),
            new DispatchOptions(), TimeProvider.System, NullLogger<MessageDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));
        long batchId = await connection.ExecuteScalarAsync<long>("SELECT NewBatchId FROM dbo.DailyResendItem WHERE OperationId=@Id", new { Id = id });
        Assert.Equal((byte)BatchStatus.Held, await connection.ExecuteScalarAsync<byte>("SELECT Status FROM dbo.MessageBatch WHERE Id=@Id", new { Id = batchId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.DispatchAcceptance"));
        Assert.Equal(0, (await handler.Status(id, admin, CancellationToken.None)).Value.Submitted);
    }
    private static Task<int> DebitCount(SqlConnection connection, short customerId) => connection.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId=@customerId AND Type=2", new { customerId });

    private async Task<(long[] Ids, ApiKeyIdentity Owner, ApiKeyIdentity Admin, string Day)> DailySources(int count)
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        short customerId = await CreateCustomerAsync("daily-owner");
        ApiKeyIdentity owner = await IssueApiKeyAsync(customerId);
        Result<SendMessagesResponse> original = await new SendMessagesHandler(_db, TimeProvider.System).Handle(new()
        {
            ClientBatchId = "daily-original", SenderLine = "30001234", MessageTypeId = 1,
            Messages = Enumerable.Range(1, count).Select(index => new SendMessageItem
                { Recipient = $"98912{index:D7}", Text = "Daily original " + index }).ToArray(),
        }, owner, CancellationToken.None);
        Assert.True(original.IsSuccess, original.Error?.Message);
        SetupAdministratorCredential credential = await new AdminApiKeyBootstrapper(_db).CreateAsync("daily-admin", null, CancellationToken.None);
        ApiKeyIdentity admin = (await new ApiKeyAuthenticator(_db, TimeProvider.System).Authenticate(
            credential.ApiKey, System.Net.IPAddress.Loopback, CancellationToken.None)).Value;
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        long[] ids = (await connection.QueryAsync<long>("SELECT Id FROM dbo.Message WHERE MessageBatchId=@Id ORDER BY Id", new { Id = original.Value.BatchId })).ToArray();
        return (ids, owner, admin, JalaliDate.FromUtc(TimeProvider.System.GetUtcNow().UtcDateTime));
    }

    private static Task AddDailyReport(SqlConnection connection, long id, byte status) => connection.ExecuteAsync("""
        INSERT dbo.DeliveryReport(SubmitDateJalali,MessageId,NormalizedStatus,RawStatusCode,ReceivedAtUtc)
        SELECT SubmitDateJalali,Id,@status,@status,SYSUTCDATETIME() FROM dbo.Message WHERE Id=@id;
        """, new { id, status });
}
