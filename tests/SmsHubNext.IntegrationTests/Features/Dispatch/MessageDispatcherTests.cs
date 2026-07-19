using System.Collections.Concurrent;
using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
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

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

public sealed class MessageDispatcherTests : IAsyncLifetime
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
    public async Task An_accepted_batch_is_submitted_and_completed()
    {
        (long batchId, short customerId) = await SendBatchAsync(messageCount: 2);

        MessageDispatcher dispatcher = Dispatcher(_ => ProviderDispatchResult.Accepted(Guid.NewGuid().ToString("N")));

        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));   // claimed + dispatched
        Assert.False(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));  // nothing left to claim

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchCompleted, batch.Value.Status);
        Assert.NotNull(batch.Value.DispatchStartedAtUtc);
        Assert.NotNull(batch.Value.FinishedAtUtc);

        Result<IReadOnlyList<BatchEvent>> events = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Contains(events.Value, e => e.EventType == MessageBatchEventType.Accepted);
        Assert.Contains(events.Value, e => e.EventType == MessageBatchEventType.DispatchStarted);
        Assert.Contains(events.Value, e => e.EventType == MessageBatchEventType.DispatchCompleted);

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.All(messages.Value, m => Assert.Equal(SendStatus.Submitted, m.Status));

        // Every message got a provider id (the future DLR-matching key).
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int withoutProviderId = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Message WHERE MessageBatchId = @Id AND ProviderMessageId IS NULL;",
            new { Id = batchId });
        Assert.Equal(0, withoutProviderId);

        // No refunds: the balance reflects only the original debit.
        Assert.Equal(8000m, await BalanceAsync(customerId));
    }

    [Fact]
    public async Task A_rejected_message_is_marked_rejected_and_refunded()
    {
        (long batchId, short customerId) = await SendBatchAsync(messageCount: 1);

        MessageDispatcher dispatcher = Dispatcher(_ => ProviderDispatchResult.Rejected(resultCode: 99, detail: "blocked"));

        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchFailed, batch.Value.Status); // all messages rejected

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.All(messages.Value, m => Assert.Equal(SendStatus.Rejected, m.Status));

        // The 1000 debit is refunded, so the balance is whole again.
        Assert.Equal(10000m, await BalanceAsync(customerId));

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int refunds = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Type = 3;",
            new { CustomerId = customerId });
        Assert.Equal(1, refunds);

        Result<IReadOnlyList<BatchEvent>> events = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Contains(events.Value, e => e.EventType == MessageBatchEventType.MessageRejected);
        Assert.Contains(events.Value, e => e.EventType == MessageBatchEventType.DispatchFailed);
    }

    [Fact]
    public async Task A_credit_exhausted_batch_is_held_then_resumed()
    {
        (long batchId, short customerId) = await SendBatchAsync(messageCount: 1);

        // First pass: provider is out of credit -> the batch is held, the message stays Queued.
        MessageDispatcher broke = Dispatcher(_ => ProviderDispatchResult.InsufficientCredit());
        Assert.True(await broke.DispatchNextBatchAsync(CancellationToken.None));

        Result<Batch> held = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.Held, held.Value.Status);
        Assert.Equal(BatchStatusReason.InsufficientProviderCredit, held.Value.StatusReason);
        Assert.Null(held.Value.FinishedAtUtc);

        Result<IReadOnlyList<BatchEvent>> heldEvents = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Contains(heldEvents.Value, e =>
            e.EventType == MessageBatchEventType.Held &&
            e.BatchStatusReason == BatchStatusReason.InsufficientProviderCredit);

        Result<IReadOnlyList<BatchMessage>> queued = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.All(queued.Value, m => Assert.Equal(SendStatus.Queued, m.Status));
        Assert.Equal(9000m, await BalanceAsync(customerId)); // still debited, not refunded

        // Second pass: credit restored. With HoldRetryDelay = 0 the held batch is immediately resumable.
        MessageDispatcher ok = Dispatcher(
            _ => ProviderDispatchResult.Accepted(Guid.NewGuid().ToString("N")),
            new DispatchOptions { HoldRetryDelay = TimeSpan.Zero });
        Assert.True(await ok.DispatchNextBatchAsync(CancellationToken.None));

        Result<Batch> done = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchCompleted, done.Value.Status);
        Assert.NotNull(done.Value.FinishedAtUtc);

        Result<IReadOnlyList<BatchEvent>> doneEvents = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Contains(doneEvents.Value, e => e.EventType == MessageBatchEventType.DispatchResumed);
        Assert.Contains(doneEvents.Value, e => e.EventType == MessageBatchEventType.DispatchCompleted);
    }

    [Fact]
    public async Task A_lost_send_response_is_confirmed_via_lookup_without_resending()
    {
        (long batchId, short customerId) = await SendBatchAsync(messageCount: 1);

        // Cycle 1: the send response is lost (transport failure) -> the message awaits confirmation.
        MessageDispatcher failing = Dispatcher(
            _ => ProviderDispatchResult.Accepted("unused"),
            sendFailure: Error.Provider("dispatch.timeout", "response lost"));
        Assert.True(await failing.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal((byte)SendStatus.AwaitingConfirmation, await MessageStatusAsync(batchId));
        Assert.Equal(9000m, await BalanceAsync(customerId)); // debited once, not refunded

        Result<IReadOnlyList<BatchEvent>> awaitingEvents = await new ListBatchEventsHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Contains(awaitingEvents.Value, e => e.EventType == MessageBatchEventType.AwaitingConfirmation);

        // Cycle 2: the provider confirms it WAS accepted -> Submitted, no resend, no extra charge.
        int sendCalls = 0;
        MessageDispatcher recovering = Dispatcher(
            _ => { sendCalls++; return ProviderDispatchResult.Accepted("should-not-be-used"); },
            options: new DispatchOptions { MinAwaitingConfirmationAge = TimeSpan.Zero },
            resolve: _ => Result.Success<string?>("magfa-555"));
        Assert.True(await recovering.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(0, sendCalls); // never re-sent
        Assert.Equal((byte)SendStatus.Submitted, await MessageStatusAsync(batchId));
        Assert.Equal("magfa-555", await ProviderMessageIdAsync(batchId)); // id recovered from the lookup
        Assert.Equal(9000m, await BalanceAsync(customerId)); // still only the original debit

        Result<Batch> done = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchCompleted, done.Value.Status);
    }

    [Fact]
    public async Task A_lost_send_response_with_no_provider_record_is_resent_after_repeated_negative_confirmation()
    {
        (long batchId, _) = await SendBatchAsync(messageCount: 1);

        MessageDispatcher failing = Dispatcher(
            _ => ProviderDispatchResult.Accepted("unused"),
            sendFailure: Error.Provider("dispatch.timeout", "response lost"));
        Assert.True(await failing.DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal((byte)SendStatus.AwaitingConfirmation, await MessageStatusAsync(batchId));

        // One negative lookup is not enough to resend; the conservative choice is to wait.
        int sendCalls = 0;
        DispatchOptions conservativeOptions = new DispatchOptions
        {
            MinAwaitingConfirmationAge = TimeSpan.Zero,
            AwaitingConfirmationRetryDelay = TimeSpan.Zero,
            RequiredNegativeConfirmations = 2,
        };
        MessageDispatcher firstNegative = Dispatcher(
            _ => { sendCalls++; return ProviderDispatchResult.Accepted("magfa-777"); },
            options: conservativeOptions,
            resolve: _ => Result.Success<string?>(null));
        Assert.True(await firstNegative.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(0, sendCalls);
        Assert.Equal((byte)SendStatus.AwaitingConfirmation, await MessageStatusAsync(batchId));

        // A repeated negative lookup after the safety window is treated as safe to resend.
        MessageDispatcher secondNegativeThenResend = Dispatcher(
            _ => { sendCalls++; return ProviderDispatchResult.Accepted("magfa-777"); },
            options: conservativeOptions,
            resolve: _ => Result.Success<string?>(null));
        Assert.True(await secondNegativeThenResend.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(1, sendCalls);
        Assert.Equal((byte)SendStatus.Submitted, await MessageStatusAsync(batchId));
        Assert.Equal("magfa-777", await ProviderMessageIdAsync(batchId));
    }

    [Fact]
    public async Task A_provider_without_idempotent_resend_is_held_after_negative_confirmations()
    {
        (long batchId, _) = await SendBatchAsync(messageCount: 1);

        MessageDispatcher failing = Dispatcher(
            _ => ProviderDispatchResult.Accepted("unused"),
            sendFailure: Error.Provider("dispatch.timeout", "response lost"),
            supportsIdempotentResend: false);
        Assert.True(await failing.DispatchNextBatchAsync(CancellationToken.None));

        int sendCalls = 0;
        DispatchOptions options = new DispatchOptions
        {
            MinAwaitingConfirmationAge = TimeSpan.Zero,
            AwaitingConfirmationRetryDelay = TimeSpan.Zero,
            RequiredNegativeConfirmations = 2,
        };
        MessageDispatcher firstNegative = Dispatcher(
            _ => { sendCalls++; return ProviderDispatchResult.Accepted("must-not-send"); },
            options,
            resolve: _ => Result.Success<string?>(null),
            supportsIdempotentResend: false);
        Assert.True(await firstNegative.DispatchNextBatchAsync(CancellationToken.None));

        MessageDispatcher secondNegative = Dispatcher(
            _ => { sendCalls++; return ProviderDispatchResult.Accepted("must-not-send"); },
            options,
            resolve: _ => Result.Success<string?>(null),
            supportsIdempotentResend: false);
        Assert.True(await secondNegative.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(0, sendCalls);
        Assert.Equal((byte)SendStatus.AwaitingConfirmation, await MessageStatusAsync(batchId));
        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.Held, batch.Value.Status);
        Assert.Equal(BatchStatusReason.ManualReviewRequired, batch.Value.StatusReason);
    }

    [Fact]
    public async Task Concurrent_dispatchers_claim_each_batch_once()
    {
        const int batchCount = 8;
        List<long> batchIds = new List<long>(batchCount);
        for (int i = 0; i < batchCount; i++)
        {
            (long batchId, _) = await SendBatchAsync(messageCount: 1);
            batchIds.Add(batchId);
        }

        ConcurrentDictionary<long, int> providerSendCounts = new ConcurrentDictionary<long, int>();
        Func<ProviderSendRequest, Result<ProviderDispatchResult>> acceptAndCount = request =>
        {
            providerSendCounts.AddOrUpdate(request.MessageId, 1, (_, count) => count + 1);
            return ProviderDispatchResult.Accepted($"provider-{request.MessageId}");
        };

        TaskCompletionSource start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Task<bool>> dispatchTasks = Enumerable.Range(0, batchCount)
            .Select(async _ =>
            {
                await start.Task;
                return await Dispatcher(acceptAndCount).DispatchNextBatchAsync(CancellationToken.None);
            })
            .ToList();

        start.SetResult();
        bool[] results = await Task.WhenAll(dispatchTasks);

        Assert.Equal(batchCount, results.Count(didWork => didWork));
        Assert.Equal(batchCount, providerSendCounts.Count);
        Assert.All(providerSendCounts.Values, count => Assert.Equal(1, count));

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int submittedMessages = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.Message
            WHERE MessageBatchId IN @BatchIds AND Status = @Status;
            """,
            new { BatchIds = batchIds, Status = (byte)SendStatus.Submitted });
        Assert.Equal(batchCount, submittedMessages);

        int completedBatches = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.MessageBatch
            WHERE Id IN @BatchIds AND Status = @Status;
            """,
            new { BatchIds = batchIds, Status = (byte)BatchStatus.DispatchCompleted });
        Assert.Equal(batchCount, completedBatches);
    }

    [Fact]
    public async Task A_stale_worker_cannot_send_a_later_chunk_after_its_lease_is_reclaimed()
    {
        (long batchId, _) = await SendBatchAsync(messageCount: 2);
        BlockingFirstSendProvider staleProvider = new BlockingFirstSendProvider();
        MessageDispatcher staleDispatcher = new MessageDispatcher(
            _db,
            new SmsProviderRegistry([staleProvider]),
            new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

        Task<bool> staleTask = staleDispatcher.DispatchNextBatchAsync(CancellationToken.None);
        await staleProvider.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using (SqlConnection connection = await _db.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.MessageBatch SET DispatchLeaseExpiresAtUtc = '2000-01-01' WHERE Id = @Id;",
                new { Id = batchId });
        }

        ConcurrentDictionary<long, int> replacementSendCounts = new ConcurrentDictionary<long, int>();
        MessageDispatcher replacement = Dispatcher(
            request =>
            {
                replacementSendCounts.AddOrUpdate(request.MessageId, 1, (_, count) => count + 1);
                return ProviderDispatchResult.Accepted($"replacement-{request.MessageId}");
            },
            new DispatchOptions { MinAwaitingConfirmationAge = TimeSpan.Zero },
            resolve: id => Result.Success<string?>($"recovered-{id}"));

        Assert.True(await replacement.DispatchNextBatchAsync(CancellationToken.None));
        staleProvider.ReleaseFirstSend.SetResult();
        Assert.True(await staleTask);

        Assert.Single(staleProvider.SentMessageIds);
        Assert.Single(replacementSendCounts);
        Assert.All(replacementSendCounts.Values, count => Assert.Equal(1, count));
        Assert.DoesNotContain(staleProvider.SentMessageIds.Single(), replacementSendCounts.Keys);

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchCompleted, batch.Value.Status);
    }

    private MessageDispatcher Dispatcher(
        Func<ProviderSendRequest, Result<ProviderDispatchResult>> behavior,
        DispatchOptions? options = null,
        Func<long, Result<string?>>? resolve = null,
        Error? sendFailure = null,
        bool supportsIdempotentResend = true)
    {
        StubProvider provider = new StubProvider(behavior, resolve, sendFailure)
        {
            SupportsIdempotentResend = supportsIdempotentResend,
        };

        return new MessageDispatcher(
            _db,
            new SmsProviderRegistry([provider]),
            options ?? new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);
    }

    private async Task<byte> MessageStatusAsync(long batchId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<byte>(
            "SELECT TOP 1 Status FROM dbo.Message WHERE MessageBatchId = @batchId ORDER BY Id;", new { batchId });
    }

    private async Task<string?> ProviderMessageIdAsync(long batchId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT TOP 1 ProviderMessageId FROM dbo.Message WHERE MessageBatchId = @batchId ORDER BY Id;", new { batchId });
    }

    private async Task<decimal> BalanceAsync(short customerId)
    {
        Result<CustomerBalance> balance = await new GetBalanceHandler(_db, TimeProvider.System).Handle(customerId, CancellationToken.None);
        return balance.Value.Balance;
    }

    private async Task<(long BatchId, short CustomerId)> SendBatchAsync(int messageCount)
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db)
            .Handle(new CreateCustomerRequest { Name = "disp", Code = $"disp-{Guid.NewGuid():N}" }, CancellationToken.None);
        short customerId = customer.Value.Id;

        await new TopUpHandler(_db)
            .Handle(new TopUpRequest { CustomerId = customerId, Amount = 10000m }, CancellationToken.None);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db)
            .Handle(new IssueApiKeyRequest { CustomerId = customerId, Name = "k" }, CancellationToken.None);

        List<SendMessageItem> items = Enumerable.Range(0, messageCount)
            .Select(i => new SendMessageItem { Recipient = $"98912000000{i}", Text = "Hello" })
            .ToList();

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = $"dispatcher-{Guid.NewGuid():N}",
                CustomerId = customerId,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = items,
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return (send.Value.BatchId, customerId);
    }

    private sealed class StubProvider : ISmsProvider
    {
        private readonly Func<ProviderSendRequest, Result<ProviderDispatchResult>> _behavior;
        private readonly Func<long, Result<string?>> _resolve;
        private readonly Error? _sendFailure;

        public StubProvider(
            Func<ProviderSendRequest, Result<ProviderDispatchResult>> behavior,
            Func<long, Result<string?>>? resolve = null,
            Error? sendFailure = null)
        {
            _behavior = behavior;
            _resolve = resolve ?? (_ => Result.Success<string?>(null));
            _sendFailure = sendFailure;
        }

        public string Name => "magfa";

        public int MaxBatchSize => 1000;

        public bool SupportsIdempotentResend { get; init; } = true;

        // Apply the per-message behavior to each request, aligned to input order — unless a whole-batch
        // (transport) failure is configured.
        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
        {
            if (_sendFailure is not null)
                return Task.FromResult(Result.Failure<IReadOnlyList<Result<ProviderDispatchResult>>>(_sendFailure));

            IReadOnlyList<Result<ProviderDispatchResult>> results = requests.Select(_behavior).ToList();
            return Task.FromResult(Result.Success(results));
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
            Task.FromResult(_resolve(messageId));

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]));

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderInboundMessage>>([]));
    }

    private sealed class BlockingFirstSendProvider : ISmsProvider
    {
        public TaskCompletionSource FirstSendStarted { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstSend { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<long> SentMessageIds { get; } = new ConcurrentBag<long>();

        public string Name => "magfa";

        public int MaxBatchSize => 1;

        public bool SupportsIdempotentResend => false;

        public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests,
            CancellationToken cancellationToken)
        {
            ProviderSendRequest request = Assert.Single(requests);
            SentMessageIds.Add(request.MessageId);

            if (SentMessageIds.Count == 1)
            {
                FirstSendStarted.SetResult();
                await ReleaseFirstSend.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<Result<ProviderDispatchResult>> results =
                [Result.Success(ProviderDispatchResult.Accepted($"stale-{request.MessageId}"))];
            return Result.Success(results);
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(
            long messageId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<string?>(null));

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]));

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderInboundMessage>>([]));
    }
}
