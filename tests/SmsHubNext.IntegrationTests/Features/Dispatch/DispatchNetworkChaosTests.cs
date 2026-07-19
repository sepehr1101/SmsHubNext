using System.Collections.Concurrent;
using System.Net.Http.Json;
using Dapper;
using DbUp.Engine;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Testcontainers.Toxiproxy;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

/// <summary>
/// Exercises crash recovery through a real TCP fault between the dispatcher and SQL Server.
/// The provider accepts the request first; Toxiproxy then drops SQL connectivity before the
/// result can be persisted. Recovery must reconcile by local message id without a second send.
/// </summary>
public sealed class DispatchNetworkChaosTests : IAsyncLifetime
{
    private const string SqlNetworkAlias = "smshub-chaos-sql";
    private const string ProxyName = "smshub_chaos_sql";
    private const ushort SqlProxyPort = ToxiproxyBuilder.FirstProxiedPort;

    private readonly INetwork _network;
    private readonly MsSqlContainer _sqlServer;
    private readonly ToxiproxyContainer _toxiproxy;
    private HttpClient _toxiproxyApi = null!;
    private Db _db = null!;

    public DispatchNetworkChaosTests()
    {
        _network = new NetworkBuilder().Build();
        _sqlServer = new MsSqlBuilder(Literals.sqlImage)
            .WithNetwork(_network)
            .WithNetworkAliases(SqlNetworkAlias)
            .Build();
        _toxiproxy = new ToxiproxyBuilder("ghcr.io/shopify/toxiproxy:2.12.0")
            .WithNetwork(_network)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _toxiproxy.StartAsync());

        _toxiproxyApi = new HttpClient
        {
            BaseAddress = new Uri(
                $"http://{_toxiproxy.Hostname}:{_toxiproxy.GetMappedPublicPort()}",
                UriKind.Absolute),
        };

        await ConfigureProxyAsync(enabled: true);

        SqlConnectionStringBuilder connectionString = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            DataSource = $"{_toxiproxy.Hostname},{_toxiproxy.GetMappedPublicPort(SqlProxyPort)}",
            ConnectTimeout = 3,
            Pooling = false,
        };

        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString.ConnectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        _db = new Db(connectionString.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _toxiproxyApi.Dispose();
        await _toxiproxy.DisposeAsync();
        await _sqlServer.DisposeAsync();
        await _network.DisposeAsync();
    }

    [Fact]
    public async Task A_sql_disconnect_after_provider_acceptance_recovers_without_resending()
    {
        (long batchId, short customerId) = await SendBatchAsync();
        RecoverableBlockingProvider provider = new RecoverableBlockingProvider();
        AdjustableTimeProvider clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        MessageDispatcher interruptedDispatcher = CreateDispatcher(provider, clock);

        Task<bool> interrupted = interruptedDispatcher.DispatchNextBatchAsync(CancellationToken.None);
        await provider.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await ConfigureProxyAsync(enabled: false);
        provider.ReleaseSend.SetResult();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await interrupted.WaitAsync(TimeSpan.FromSeconds(20)));

        await ConfigureProxyAsync(enabled: true);
        await WaitForSqlRecoveryAsync();
        clock.Advance(TimeSpan.FromMinutes(6));

        MessageDispatcher recoveringDispatcher = CreateDispatcher(provider, clock);
        Assert.True(await recoveringDispatcher.DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(1, provider.SendCallCount);
        Assert.False(await recoveringDispatcher.DispatchNextBatchAsync(CancellationToken.None));

        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(BatchStatus.DispatchCompleted, batch.Value.Status);

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        DispatchInvariantState state = await connection.QuerySingleAsync<DispatchInvariantState>(
            """
            SELECT
                m.Status AS MessageStatus,
                COUNT(DISTINCT p.MessageId) AS PollRows,
                cb.Balance,
                SUM(CASE WHEN bt.Type = 2 THEN 1 ELSE 0 END) AS DebitRows
            FROM dbo.Message m
            INNER JOIN dbo.CustomerBalance cb ON cb.CustomerId = m.CustomerId
            LEFT JOIN dbo.DeliveryReportPoll p ON p.MessageId = m.Id
            LEFT JOIN dbo.BalanceTransaction bt ON bt.MessageBatchId = m.MessageBatchId
            WHERE m.MessageBatchId = @BatchId AND cb.CustomerId = @CustomerId
            GROUP BY m.Status, cb.Balance;
            """,
            new { BatchId = batchId, CustomerId = customerId });

        Assert.Equal((byte)SendStatus.Submitted, state.MessageStatus);
        Assert.Equal(1, state.PollRows);
        Assert.Equal(9000m, state.Balance);
        Assert.Equal(1, state.DebitRows);
    }

    private MessageDispatcher CreateDispatcher(ISmsProvider provider, TimeProvider clock) =>
        new MessageDispatcher(
            _db,
            new SmsProviderRegistry([provider]),
            new DispatchOptions(),
            clock,
            NullLogger<MessageDispatcher>.Instance);

    private async Task<(long BatchId, short CustomerId)> SendBatchAsync()
    {
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest
            {
                Name = "network-chaos",
                Code = $"network-chaos-{Guid.NewGuid():N}",
            },
            CancellationToken.None);
        Assert.True(customer.IsSuccess, customer.Error?.Message);

        Result<TopUpResponse> topUp = await new TopUpHandler(_db).Handle(
            new TopUpRequest { CustomerId = customer.Value.Id, Amount = 10000m },
            CancellationToken.None);
        Assert.True(topUp.IsSuccess, topUp.Error?.Message);

        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "network-chaos" },
            CancellationToken.None);
        Assert.True(key.IsSuccess, key.Error?.Message);

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                ClientBatchId = $"network-chaos-{Guid.NewGuid():N}",
                SenderLine = "30001234",
                MessageTypeId = 1,
                Messages = [new SendMessageItem { Recipient = "989120000099", Text = "Chaos" }],
            },
            new ApiKeyIdentity(key.Value.Id, customer.Value.Id, key.Value.KeyPrefix),
            CancellationToken.None);
        Assert.True(send.IsSuccess, send.Error?.Message);

        return (send.Value.BatchId, customer.Value.Id);
    }

    private async Task ConfigureProxyAsync(bool enabled)
    {
        ProxyConfiguration configuration = new ProxyConfiguration(
            ProxyName,
            $"0.0.0.0:{SqlProxyPort}",
            $"{SqlNetworkAlias}:1433",
            enabled);

        using HttpResponseMessage response = await _toxiproxyApi.PostAsJsonAsync(
            enabled && !await ProxyExistsAsync()
                ? "/proxies"
                : $"/proxies/{ProxyName}",
            configuration);
        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> ProxyExistsAsync()
    {
        using HttpResponseMessage response = await _toxiproxyApi.GetAsync($"/proxies/{ProxyName}");
        return response.IsSuccessStatusCode;
    }

    private async Task WaitForSqlRecoveryAsync()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await using SqlConnection connection = await _db.OpenConnectionAsync();
                await connection.ExecuteScalarAsync<int>("SELECT 1;");
                return;
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        throw new InvalidOperationException("SQL Server did not recover after the Toxiproxy link was restored.", lastError);
    }

    private sealed class RecoverableBlockingProvider : ISmsProvider
    {
        private readonly ConcurrentDictionary<long, string> _accepted = new ConcurrentDictionary<long, string>();
        private int _sendCallCount;

        public TaskCompletionSource SendStarted { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSend { get; } =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCallCount => Volatile.Read(ref _sendCallCount);

        public string Name => "magfa";

        public int MaxBatchSize => 1;

        public bool SupportsIdempotentResend => false;

        public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests,
            CancellationToken cancellationToken)
        {
            ProviderSendRequest request = Assert.Single(requests);
            Interlocked.Increment(ref _sendCallCount);
            string providerMessageId = $"accepted-{request.MessageId}";
            _accepted[request.MessageId] = providerMessageId;
            SendStarted.SetResult();
            await ReleaseSend.Task.WaitAsync(cancellationToken);

            IReadOnlyList<Result<ProviderDispatchResult>> results =
                [Result.Success(ProviderDispatchResult.Accepted(providerMessageId))];
            return Result.Success(results);
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(
            long messageId,
            CancellationToken cancellationToken)
        {
            _accepted.TryGetValue(messageId, out string? providerMessageId);
            return Task.FromResult(Result.Success(providerMessageId));
        }

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderDeliveryReport>>([]));

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProviderInboundMessage>>([]));
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public AdjustableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed record ProxyConfiguration(string Name, string Listen, string Upstream, bool Enabled);

    private sealed record DispatchInvariantState(byte MessageStatus, int PollRows, decimal Balance, int DebitRows);
}
