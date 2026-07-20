using System.Diagnostics;
using System.Text.Json;
using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Kavenegar;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace SmsHubNext.IntegrationTests.Features.Providers;

/// <summary>
/// An explicitly opted-in end-to-end probe: 10,000 synthetic messages are priced, debited and
/// persisted in a disposable SQL Server, then submitted to the selected provider's real HTTPS
/// endpoint with randomly generated invalid credentials. No valid credential is read or accepted.
///
/// A request-level authentication failure is conservatively an unknown submission outcome in the
/// current dispatcher. The test therefore verifies that every message is parked as
/// AwaitingConfirmation, every batch is held for manual review, no delivery polling is scheduled,
/// and no automatic refund or blind resend happens.
/// </summary>
public sealed class InvalidCredentialsLiveTests : IAsyncLifetime
{
    private const int TotalMessageCount = 10_000;
    private const decimal InitialBalance = 20_000_000m;
    private const decimal PricePerSegment = 1_000m;
    private const string ProviderVariable = "SMSHUBNEXT_LIVE_PROVIDER";
    private const string ReportPathVariable = "SMSHUBNEXT_LIVE_REPORT_PATH";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private readonly ITestOutputHelper _output;
    private Db _db = null!;

    public InvalidCredentialsLiveTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();
        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        _db = new Db(connectionString);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [LiveProviderFact]
    [Trait("Category", "LiveProvider")]
    public async Task Ten_thousand_messages_with_invalid_credentials_are_held_and_audited()
    {
        string providerCode = RequiredProviderCode();
        LiveScenario scenario = ScenarioFor(providerCode);
        TestIdentity identity = await SeedScenarioAsync(scenario);

        using HttpClient httpClient = new HttpClient
        {
            BaseAddress = scenario.BaseUri,
            Timeout = TimeSpan.FromSeconds(15),
        };
        ISmsProvider liveProvider = CreateProvider(scenario, httpClient);
        CountingSmsProvider provider = new CountingSmsProvider(liveProvider);

        DateTime startedAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        Stopwatch totalTimer = Stopwatch.StartNew();
        Stopwatch persistenceTimer = Stopwatch.StartNew();
        IReadOnlyList<long> batchIds = await PersistMessagesAsync(scenario, identity);
        persistenceTimer.Stop();

        Stopwatch dispatchTimer = Stopwatch.StartNew();
        MessageDispatcher dispatcher = new MessageDispatcher(
            _db,
            new SmsProviderRegistry([provider]),
            new DispatchOptions
            {
                // An authentication failure must become operator-visible after the first real call;
                // this probe must not hammer the provider or retry an unknown submission.
                MaxDispatchAttempts = 1,
            },
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

        foreach (long _ in batchIds)
            Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));

        Assert.False(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));
        dispatchTimer.Stop();
        totalTimer.Stop();

        DatabaseProbeState state = await ReadStateAsync(identity.CustomerId);
        AssertProbeInvariants(scenario, batchIds.Count, provider, state);

        LiveProbeReport report = new LiveProbeReport(
            providerCode,
            startedAtUtc,
            TotalMessageCount,
            batchIds.Count,
            provider.SendCallCount,
            provider.MessagesOfferedToProvider,
            provider.FailedSendCallCount,
            persistenceTimer.Elapsed.TotalMilliseconds,
            dispatchTimer.Elapsed.TotalMilliseconds,
            totalTimer.Elapsed.TotalMilliseconds,
            state.Messages.AwaitingConfirmation,
            state.Messages.Queued,
            state.Messages.Submitted,
            state.Messages.Rejected,
            state.Batches.HeldForManualReview,
            state.Ledger.DebitRows,
            state.Ledger.RefundRows,
            state.Ledger.Balance,
            "HeldForManualReview");

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        _output.WriteLine(json);
        WriteReportIfRequested(json);
    }

    private static string RequiredProviderCode()
    {
        string providerCode = Environment.GetEnvironmentVariable(ProviderVariable)?.Trim().ToLowerInvariant()
            ?? string.Empty;
        if (providerCode is not (ProviderCodes.Magfa or ProviderCodes.Kavenegar))
        {
            throw new InvalidOperationException(
                $"{ProviderVariable} must be either '{ProviderCodes.Magfa}' or '{ProviderCodes.Kavenegar}'.");
        }

        return providerCode;
    }

    private static LiveScenario ScenarioFor(string providerCode) => providerCode switch
    {
        ProviderCodes.Magfa => new LiveScenario(
            ProviderCodes.Magfa,
            ProviderId: 1,
            SenderLine: "300099999999",
            BatchSize: MagfaOptions.MaxMessagesPerRequest,
            new Uri("https://sms.magfa.com", UriKind.Absolute)),
        ProviderCodes.Kavenegar => new LiveScenario(
            ProviderCodes.Kavenegar,
            ProviderId: 2,
            SenderLine: "100099999999",
            BatchSize: KavenegarOptions.MaxMessagesPerRequest,
            new Uri("https://api.kavenegar.com", UriKind.Absolute)),
        _ => throw new InvalidOperationException($"Unsupported live provider '{providerCode}'."),
    };

    private static ISmsProvider CreateProvider(LiveScenario scenario, HttpClient httpClient)
    {
        string randomInvalidCredential = $"smshub-invalid-{Guid.NewGuid():N}";
        if (scenario.ProviderCode == ProviderCodes.Magfa)
        {
            MagfaAccount account = new MagfaAccount
            {
                Username = randomInvalidCredential,
                Domain = "invalid",
                Password = Guid.NewGuid().ToString("N"),
                SenderLines = [scenario.SenderLine],
            };
            MagfaOptions options = new MagfaOptions
            {
                Enabled = true,
                BaseUrl = scenario.BaseUri.ToString(),
                BatchSize = scenario.BatchSize,
                Timeout = httpClient.Timeout,
            };
            return new MagfaSmsProvider(
                httpClient,
                new MagfaAccountResolver([account]),
                options,
                NullLogger<MagfaSmsProvider>.Instance);
        }

        KavenegarAccount kavenegarAccount = new KavenegarAccount
        {
            ApiKey = randomInvalidCredential,
            SenderLines = [scenario.SenderLine],
        };
        KavenegarOptions kavenegarOptions = new KavenegarOptions
        {
            Enabled = true,
            BaseUrl = scenario.BaseUri.ToString(),
            BatchSize = scenario.BatchSize,
            Timeout = httpClient.Timeout,
        };
        return new KavenegarSmsProvider(
            httpClient,
            new KavenegarAccountResolver([kavenegarAccount]),
            kavenegarOptions,
            NullLogger<KavenegarSmsProvider>.Instance);
    }

    private async Task<TestIdentity> SeedScenarioAsync(LiveScenario scenario)
    {
        await ReferenceDataTestData.EnsureDefaultsAsync(_db);

        await using (SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None))
        {
            int providerAccountId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO dbo.ProviderAccount
                    (ProviderId, DisplayName, AuthType, SettingsJson, SecretEncrypted, IsActive)
                OUTPUT INSERTED.Id
                VALUES
                    (@ProviderId, N'Invalid credential live probe', @AuthType, @SettingsJson, 0x01, 1);
                """,
                new
                {
                    scenario.ProviderId,
                    AuthType = scenario.ProviderCode == ProviderCodes.Magfa
                        ? ProviderAccountAuthTypes.UsernamePasswordDomain
                        : ProviderAccountAuthTypes.ApiKey,
                    SettingsJson = scenario.ProviderCode == ProviderCodes.Magfa
                        ? "{\"username\":\"invalid\",\"domain\":\"invalid\"}"
                        : "{}",
                },
                cancellationToken: CancellationToken.None));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.SenderLine
                    (ProviderId, LineNumber, IsSharedLine, IsActive, ProviderAccountId)
                VALUES
                    (@ProviderId, @SenderLine, 1, 1, @ProviderAccountId);

                INSERT INTO dbo.Tariff
                    (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, Currency, IsActive)
                VALUES
                    (@ProviderId, NULL, 0, '2025-01-01T00:00:00', 'IRR', 1);

                DECLARE @TariffId INT = CONVERT(INT, SCOPE_IDENTITY());
                INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
                VALUES (@TariffId, 0, NULL, @PricePerSegment);
                """,
                new
                {
                    scenario.ProviderId,
                    scenario.SenderLine,
                    ProviderAccountId = providerAccountId,
                    PricePerSegment,
                },
                cancellationToken: CancellationToken.None));
        }

        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest
            {
                Name = "Invalid credential live probe",
                Code = $"live-{scenario.ProviderCode}-{Guid.NewGuid():N}"[..32],
            },
            CancellationToken.None);
        Assert.True(customer.IsSuccess, customer.Error?.Message);

        Result<TopUpResponse> topUp = await new TopUpHandler(_db).Handle(
            new TopUpRequest
            {
                CustomerId = customer.Value.Id,
                Amount = InitialBalance,
                Reference = $"live-invalid-{Guid.NewGuid():N}",
            },
            CancellationToken.None);
        Assert.True(topUp.IsSuccess, topUp.Error?.Message);

        Result<IssueApiKeyResponse> apiKey = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "live-invalid-probe" },
            CancellationToken.None);
        Assert.True(apiKey.IsSuccess, apiKey.Error?.Message);

        return new TestIdentity(
            customer.Value.Id,
            new ApiKeyIdentity(apiKey.Value.Id, customer.Value.Id, apiKey.Value.KeyPrefix));
    }

    private async Task<IReadOnlyList<long>> PersistMessagesAsync(LiveScenario scenario, TestIdentity identity)
    {
        int batchCount = TotalMessageCount / scenario.BatchSize;
        Assert.Equal(0, TotalMessageCount % scenario.BatchSize);

        SendMessagesHandler handler = new SendMessagesHandler(_db, TimeProvider.System);
        List<long> batchIds = new List<long>(batchCount);
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            int firstMessageIndex = batchIndex * scenario.BatchSize;
            List<SendMessageItem> messages = Enumerable.Range(firstMessageIndex, scenario.BatchSize)
                .Select(index => new SendMessageItem
                {
                    Recipient = $"98910{index:D7}",
                    Text = $"SmsHubNext invalid credential probe {index}",
                    ClientCorrelatedId = $"live-invalid-{index}",
                })
                .ToList();

            Result<SendMessagesResponse> send = await handler.Handle(
                new SendMessagesRequest
                {
                    SenderLine = scenario.SenderLine,
                    MessageTypeId = 1,
                    ClientBatchId = $"live-invalid-{scenario.ProviderCode}-{Guid.NewGuid():N}",
                    Messages = messages,
                },
                identity.ApiKeyIdentity,
                CancellationToken.None);

            Assert.True(send.IsSuccess, send.Error?.Message);
            Assert.Equal(scenario.BatchSize, send.Value.AcceptedCount);
            batchIds.Add(send.Value.BatchId);
        }

        return batchIds;
    }

    private async Task<DatabaseProbeState> ReadStateAsync(short customerId)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);

        MessageProbeState messages = await connection.QuerySingleAsync<MessageProbeState>(new CommandDefinition(
            """
            SELECT
                COUNT_BIG(*) AS Total,
                SUM(CONVERT(BIGINT, CASE WHEN Status = @Queued THEN 1 ELSE 0 END)) AS Queued,
                SUM(CONVERT(BIGINT, CASE WHEN Status = @Submitted THEN 1 ELSE 0 END)) AS Submitted,
                SUM(CONVERT(BIGINT, CASE WHEN Status = @Rejected THEN 1 ELSE 0 END)) AS Rejected,
                SUM(CONVERT(BIGINT, CASE WHEN Status = @AwaitingConfirmation THEN 1 ELSE 0 END)) AS AwaitingConfirmation,
                SUM(TotalCost) AS TotalCost,
                SUM(CONVERT(BIGINT, CASE WHEN ProviderMessageId IS NOT NULL THEN 1 ELSE 0 END)) AS WithProviderMessageId
            FROM dbo.Message
            WHERE CustomerId = @CustomerId;
            """,
            new
            {
                CustomerId = customerId,
                Queued = (byte)SendStatus.Queued,
                Submitted = (byte)SendStatus.Submitted,
                Rejected = (byte)SendStatus.Rejected,
                AwaitingConfirmation = (byte)SendStatus.AwaitingConfirmation,
            },
            cancellationToken: CancellationToken.None));

        BatchProbeState batches = await connection.QuerySingleAsync<BatchProbeState>(new CommandDefinition(
            """
            SELECT
                COUNT_BIG(*) AS Total,
                SUM(CONVERT(BIGINT, CASE WHEN Status = @Held AND StatusReason = @ManualReview THEN 1 ELSE 0 END))
                    AS HeldForManualReview,
                SUM(CONVERT(BIGINT, DispatchAttemptCount)) AS DispatchAttempts,
                SUM(CONVERT(BIGINT, CASE WHEN FinishedAtUtc IS NOT NULL THEN 1 ELSE 0 END)) AS Finished
            FROM dbo.MessageBatch
            WHERE CustomerId = @CustomerId;
            """,
            new
            {
                CustomerId = customerId,
                Held = (byte)BatchStatus.Held,
                ManualReview = (byte)BatchStatusReason.ManualReviewRequired,
            },
            cancellationToken: CancellationToken.None));

        EventProbeState events = await connection.QuerySingleAsync<EventProbeState>(new CommandDefinition(
            """
            SELECT
                SUM(CONVERT(BIGINT, CASE WHEN e.EventType = @AwaitingConfirmation THEN 1 ELSE 0 END))
                    AS AwaitingConfirmation,
                SUM(CONVERT(BIGINT, CASE WHEN e.EventType = @Held THEN 1 ELSE 0 END)) AS Held
            FROM dbo.MessageBatchEvent e
            INNER JOIN dbo.MessageBatch b ON b.Id = e.MessageBatchId
            WHERE b.CustomerId = @CustomerId;
            """,
            new
            {
                CustomerId = customerId,
                AwaitingConfirmation = (byte)MessageBatchEventType.AwaitingConfirmation,
                Held = (byte)MessageBatchEventType.Held,
            },
            cancellationToken: CancellationToken.None));

        LedgerProbeState ledger = await connection.QuerySingleAsync<LedgerProbeState>(new CommandDefinition(
            """
            SELECT
                cb.Balance,
                SUM(CONVERT(BIGINT, CASE WHEN bt.Type = @Debit THEN 1 ELSE 0 END)) AS DebitRows,
                SUM(CONVERT(BIGINT, CASE WHEN bt.Type = @Refund THEN 1 ELSE 0 END)) AS RefundRows
            FROM dbo.CustomerBalance cb
            INNER JOIN dbo.BalanceTransaction bt ON bt.CustomerId = cb.CustomerId
            WHERE cb.CustomerId = @CustomerId
            GROUP BY cb.Balance;
            """,
            new
            {
                CustomerId = customerId,
                Debit = (byte)BalanceTransactionType.Debit,
                Refund = (byte)BalanceTransactionType.Refund,
            },
            cancellationToken: CancellationToken.None));

        long bodyRows = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT_BIG(*)
            FROM dbo.MessageBody body
            INNER JOIN dbo.Message message ON message.Id = body.Id
            WHERE message.CustomerId = @CustomerId;
            """,
            new { CustomerId = customerId },
            cancellationToken: CancellationToken.None));

        long deliveryPollRows = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT_BIG(*)
            FROM dbo.DeliveryReportPoll poll
            INNER JOIN dbo.Message message ON message.Id = poll.MessageId
            WHERE message.CustomerId = @CustomerId;
            """,
            new { CustomerId = customerId },
            cancellationToken: CancellationToken.None));

        return new DatabaseProbeState(messages, batches, events, ledger, bodyRows, deliveryPollRows);
    }

    private static void AssertProbeInvariants(
        LiveScenario scenario,
        int expectedBatchCount,
        CountingSmsProvider provider,
        DatabaseProbeState state)
    {
        Assert.Equal(expectedBatchCount, provider.SendCallCount);
        Assert.Equal(expectedBatchCount, provider.FailedSendCallCount);
        Assert.Equal(TotalMessageCount, provider.MessagesOfferedToProvider);

        Assert.Equal(TotalMessageCount, state.Messages.Total);
        Assert.Equal(TotalMessageCount, state.Messages.AwaitingConfirmation);
        Assert.Equal(0, state.Messages.Queued);
        Assert.Equal(0, state.Messages.Submitted);
        Assert.Equal(0, state.Messages.Rejected);
        Assert.Equal(TotalMessageCount * PricePerSegment, state.Messages.TotalCost);
        Assert.Equal(0, state.Messages.WithProviderMessageId);
        Assert.Equal(TotalMessageCount, state.BodyRows);
        Assert.Equal(0, state.DeliveryPollRows);

        Assert.Equal(expectedBatchCount, state.Batches.Total);
        Assert.Equal(expectedBatchCount, state.Batches.HeldForManualReview);
        Assert.Equal(expectedBatchCount, state.Batches.DispatchAttempts);
        Assert.Equal(0, state.Batches.Finished);
        Assert.Equal(expectedBatchCount, state.Events.AwaitingConfirmation);
        Assert.Equal(expectedBatchCount, state.Events.Held);

        Assert.Equal(expectedBatchCount, state.Ledger.DebitRows);
        Assert.Equal(0, state.Ledger.RefundRows);
        Assert.Equal(InitialBalance - (TotalMessageCount * PricePerSegment), state.Ledger.Balance);
        Assert.Equal(TotalMessageCount / scenario.BatchSize, expectedBatchCount);
    }

    private static void WriteReportIfRequested(string json)
    {
        string? reportPath = Environment.GetEnvironmentVariable(ReportPathVariable);
        if (string.IsNullOrWhiteSpace(reportPath))
            return;

        string fullPath = Path.GetFullPath(reportPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, json);
    }

    private sealed class CountingSmsProvider : ISmsProvider
    {
        private readonly ISmsProvider _inner;

        public CountingSmsProvider(ISmsProvider inner) => _inner = inner;

        public string Name => _inner.Name;
        public int MaxBatchSize => _inner.MaxBatchSize;
        public bool SupportsIdempotentResend => _inner.SupportsIdempotentResend;
        public int SendCallCount { get; private set; }
        public int FailedSendCallCount { get; private set; }
        public int MessagesOfferedToProvider { get; private set; }

        public async Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests,
            CancellationToken cancellationToken)
        {
            SendCallCount++;
            MessagesOfferedToProvider += requests.Count;
            Result<IReadOnlyList<Result<ProviderDispatchResult>>> result =
                await _inner.SendBatchAsync(requests, cancellationToken);
            if (result.IsFailure)
                FailedSendCallCount++;
            return result;
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(
            long messageId,
            CancellationToken cancellationToken) =>
            _inner.ResolveSubmittedMessageIdAsync(messageId, cancellationToken);

        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(
            IReadOnlyCollection<string> providerMessageIds,
            CancellationToken cancellationToken) =>
            _inner.GetDeliveryReportsAsync(providerMessageIds, cancellationToken);

        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(
            int maxCount,
            CancellationToken cancellationToken) =>
            _inner.FetchInboundMessagesAsync(maxCount, cancellationToken);
    }

    private sealed record LiveScenario(
        string ProviderCode,
        byte ProviderId,
        string SenderLine,
        int BatchSize,
        Uri BaseUri);

    private sealed record TestIdentity(short CustomerId, ApiKeyIdentity ApiKeyIdentity);

    private sealed record MessageProbeState(
        long Total,
        long Queued,
        long Submitted,
        long Rejected,
        long AwaitingConfirmation,
        decimal TotalCost,
        long WithProviderMessageId);

    private sealed record BatchProbeState(
        long Total,
        long HeldForManualReview,
        long DispatchAttempts,
        long Finished);

    private sealed record EventProbeState(long AwaitingConfirmation, long Held);

    private sealed record LedgerProbeState(decimal Balance, long DebitRows, long RefundRows);

    private sealed record DatabaseProbeState(
        MessageProbeState Messages,
        BatchProbeState Batches,
        EventProbeState Events,
        LedgerProbeState Ledger,
        long BodyRows,
        long DeliveryPollRows);

    private sealed record LiveProbeReport(
        string Provider,
        DateTime StartedAtUtc,
        int RequestedMessages,
        int LocalBatches,
        int ProviderHttpCalls,
        int MessagesOfferedToProvider,
        int FailedProviderCalls,
        double PersistenceMilliseconds,
        double DispatchMilliseconds,
        double TotalMilliseconds,
        long AwaitingConfirmationMessages,
        long QueuedMessages,
        long SubmittedMessages,
        long RejectedMessages,
        long HeldBatches,
        long DebitRows,
        long RefundRows,
        decimal BalanceAfter,
        string Outcome);
}
