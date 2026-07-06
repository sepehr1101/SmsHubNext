using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.DeliveryReports;
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

namespace SmsHubNext.IntegrationTests.Features.DeliveryReports;

/// <summary>
/// End-to-end delivery-report polling: a dispatched message is enqueued for polling, then the
/// poller projects the provider's DLR onto Message.DeliveryStatus, appends a DeliveryReport, and
/// dequeues — covering the terminal, in-flight, and window-expiry paths.
/// </summary>
public sealed class DeliveryReportPollerTests : IAsyncLifetime
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
    public async Task A_dispatched_message_is_enqueued_for_polling()
    {
        long messageId = await SubmitOneAsync("mid-1");

        Assert.Equal(1, await PollRowCountAsync(messageId));
        Assert.Equal((byte)DeliveryStatus.Pending, await DeliveryStatusAsync(messageId));
    }

    [Fact]
    public async Task A_delivered_report_projects_the_read_model_appends_history_and_dequeues()
    {
        long messageId = await SubmitOneAsync("mid-1");

        DeliveryReportPoller poller = Poller(_ =>
            [new ProviderDeliveryReport("mid-1", DeliveryReportStatus.Delivered, RawStatusCode: 1)]);
        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));   // claimed + applied
        Assert.False(await poller.PollNextBatchAsync(CancellationToken.None));  // queue now empty

        Assert.Equal((byte)DeliveryStatus.Delivered, await DeliveryStatusAsync(messageId));
        Assert.NotNull(await DeliveredAtAsync(messageId));
        Assert.Equal(0, await PollRowCountAsync(messageId));                    // dequeued
        Assert.Equal(1, await ReportCountAsync(messageId, DeliveryReportStatus.Delivered));
    }

    [Fact]
    public async Task An_in_flight_report_keeps_the_message_pending_and_queued()
    {
        long messageId = await SubmitOneAsync("mid-1");

        // Status null = still in flight; the message must stay Pending and queued.
        DeliveryReportPoller poller = Poller(_ =>
            [new ProviderDeliveryReport("mid-1", Status: null, RawStatusCode: 8)]);
        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));

        Assert.Equal((byte)DeliveryStatus.Pending, await DeliveryStatusAsync(messageId));
        Assert.Equal(1, await PollRowCountAsync(messageId));
        Assert.Equal(0, await ReportCountAsync(messageId, DeliveryReportStatus.Delivered));
    }

    [Fact]
    public async Task A_message_past_its_status_window_is_expired_without_a_provider_call()
    {
        long messageId = await SubmitOneAsync("mid-1");

        bool providerCalled = false;
        DeliveryReportPoller poller = Poller(
            _ =>
            {
                providerCalled = true;
                return [];
            },
            // Zero window: any dispatched-at is already past, so the row expires up front.
            new DeliveryReportPollOptions { StatusWindow = TimeSpan.Zero });

        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));

        Assert.False(providerCalled);
        Assert.Equal((byte)DeliveryStatus.Expired, await DeliveryStatusAsync(messageId));
        Assert.Equal(0, await PollRowCountAsync(messageId));
        Assert.Equal(1, await ReportCountAsync(messageId, DeliveryReportStatus.Expired));
    }

    // --- helpers ---------------------------------------------------------------------------

    private DeliveryReportPoller Poller(
        Func<IReadOnlyCollection<string>, IReadOnlyList<ProviderDeliveryReport>> dlrBehavior,
        DeliveryReportPollOptions? options = null) =>
        new(
            _db,
            new SmsProviderRegistry([new StubProvider(dlrBehavior)]),
            options ?? new DeliveryReportPollOptions(),
            TimeProvider.System,
            NullLogger<DeliveryReportPoller>.Instance);

    /// <summary>Sends one message and dispatches it (accepted with <paramref name="providerMessageId"/>),
    /// leaving it Submitted and enqueued for polling. Returns the message id.</summary>
    private async Task<long> SubmitOneAsync(string providerMessageId)
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToSeededLineAsync(_db);
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
                Messages = [new SendMessageItem { Recipient = "989120000000", Text = "Hello" }],
            },
            new ApiKeyIdentity(key.Value.Id, customerId, key.Value.KeyPrefix),
            CancellationToken.None);
        Assert.True(send.IsSuccess, send.Error?.Message);

        MessageDispatcher dispatcher = new(
            _db,
            new SmsProviderRegistry([new StubProvider(_ => [], () => ProviderDispatchResult.Accepted(providerMessageId))]),
            new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT Id FROM dbo.Message WHERE MessageBatchId = @BatchId;", new { send.Value.BatchId });
    }

    private async Task<int> PollRowCountAsync(long messageId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReportPoll WHERE MessageId = @messageId;", new { messageId });
    }

    private async Task<byte> DeliveryStatusAsync(long messageId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<byte>(
            "SELECT DeliveryStatus FROM dbo.Message WHERE Id = @messageId;", new { messageId });
    }

    private async Task<DateTime?> DeliveredAtAsync(long messageId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT DeliveredAtUtc FROM dbo.Message WHERE Id = @messageId;", new { messageId });
    }

    private async Task<int> ReportCountAsync(long messageId, DeliveryReportStatus status)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReport WHERE MessageId = @messageId AND NormalizedStatus = @status;",
            new { messageId, status = (byte)status });
    }

    private sealed class StubProvider : ISmsProvider
    {
        private readonly Func<IReadOnlyCollection<string>, IReadOnlyList<ProviderDeliveryReport>> _dlr;
        private readonly Func<ProviderDispatchResult> _send;

        public StubProvider(
            Func<IReadOnlyCollection<string>, IReadOnlyList<ProviderDeliveryReport>> dlr,
            Func<ProviderDispatchResult>? send = null)
        {
            _dlr = dlr;
            _send = send ?? (() => ProviderDispatchResult.Accepted(Guid.NewGuid().ToString("N")));
        }

        public string Name => "magfa";

        public int MaxBatchSize => 1000;

        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
        {
            IReadOnlyList<Result<ProviderDispatchResult>> results =
                requests.Select(_ => Result.Success(_send())).ToList();
            return Task.FromResult(Result.Success(results));
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<string?>(null));

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(_dlr(providerMessageIds)));

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderInboundMessage>>([]));
    }
}
