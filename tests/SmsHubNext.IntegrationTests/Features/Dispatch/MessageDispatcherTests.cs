using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

public sealed class MessageDispatcherTests : IAsyncLifetime
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
    public async Task An_accepted_batch_is_submitted_and_completed()
    {
        (long batchId, short customerId) = await SendBatchAsync(messageCount: 2);

        MessageDispatcher dispatcher = Dispatcher(_ => ProviderDispatchResult.Accepted(Guid.NewGuid().ToString("N")));

        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));   // claimed + dispatched
        Assert.False(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));  // nothing left to claim

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.Completed, batch.Value.Status);
        Assert.NotNull(batch.Value.DispatchStartedAtUtc);
        Assert.NotNull(batch.Value.FinishedAtUtc);

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
        Assert.Equal(BatchStatus.Failed, batch.Value.Status); // all messages rejected

        Result<IReadOnlyList<BatchMessage>> messages = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.All(messages.Value, m => Assert.Equal(SendStatus.Rejected, m.Status));

        // The 1000 debit is refunded, so the balance is whole again.
        Assert.Equal(10000m, await BalanceAsync(customerId));

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        int refunds = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId = @CustomerId AND Type = 3;",
            new { CustomerId = customerId });
        Assert.Equal(1, refunds);
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

        Result<IReadOnlyList<BatchMessage>> queued = await new ListBatchMessagesHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.All(queued.Value, m => Assert.Equal(SendStatus.Queued, m.Status));
        Assert.Equal(9000m, await BalanceAsync(customerId)); // still debited, not refunded

        // Second pass: credit restored. With HoldRetryDelay = 0 the held batch is immediately resumable.
        MessageDispatcher ok = Dispatcher(
            _ => ProviderDispatchResult.Accepted(Guid.NewGuid().ToString("N")),
            new DispatchOptions { HoldRetryDelay = TimeSpan.Zero });
        Assert.True(await ok.DispatchNextBatchAsync(CancellationToken.None));

        Result<Batch> done = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.Completed, done.Value.Status);
        Assert.NotNull(done.Value.FinishedAtUtc);
    }

    private MessageDispatcher Dispatcher(
        Func<ProviderSendRequest, Result<ProviderDispatchResult>> behavior,
        DispatchOptions? options = null) =>
        new(
            _db,
            new StubProvider(behavior),
            options ?? new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

    private async Task<decimal> BalanceAsync(short customerId)
    {
        Result<CustomerBalance> balance = await new GetBalanceHandler(_db).Handle(customerId, CancellationToken.None);
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

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db).Handle(
            new SendMessagesRequest
            {
                CustomerId = customerId,
                ApiKeyId = key.Value.Id,
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = items,
            },
            CancellationToken.None);

        Assert.True(send.IsSuccess, send.Error?.Message);
        return (send.Value.BatchId, customerId);
    }

    private sealed class StubProvider : ISmsProvider
    {
        private readonly Func<ProviderSendRequest, Result<ProviderDispatchResult>> _behavior;

        public StubProvider(Func<ProviderSendRequest, Result<ProviderDispatchResult>> behavior) =>
            _behavior = behavior;

        public string Name => "stub";

        public int MaxBatchSize => 1000;

        // Apply the per-message behavior to each request, aligned to input order.
        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
        {
            IReadOnlyList<Result<ProviderDispatchResult>> results = requests.Select(_behavior).ToList();
            return Task.FromResult(Result.Success(results));
        }

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]));
    }
}
