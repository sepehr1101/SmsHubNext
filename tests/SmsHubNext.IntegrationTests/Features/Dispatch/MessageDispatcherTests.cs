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
using SmsHubNext.Features.ReferenceData;
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

    private MessageDispatcher Dispatcher(
        Func<ProviderSendRequest, Result<ProviderDispatchResult>> behavior,
        DispatchOptions? options = null,
        Func<long, Result<string?>>? resolve = null,
        Error? sendFailure = null) =>
        new(
            _db,
            new SmsProviderRegistry([new StubProvider(behavior, resolve, sendFailure)]),
            options ?? new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

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
}
