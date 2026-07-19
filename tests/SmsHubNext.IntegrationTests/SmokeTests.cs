using Dapper;
using DbUp.Engine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.ProviderAccounts;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.Sending;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests;

public sealed class SmokeTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private Db _db = null!;
    private ISecretProtector _secretProtector = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();

        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);

        _db = new Db(connectionString);
        await ReferenceDataTestData.EnsureDefaultsAsync(_db);
        _secretProtector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Fresh_database_can_be_configured_and_accept_a_real_send_workflow()
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(
            new CreateCustomerRequest
            {
                Name = "First Deployment Customer",
                Code = "first-deployment-customer",
            },
            CancellationToken.None);
        Assert.True(customer.IsSuccess, customer.Error?.Message);

        Result<TopUpResponse> topUp = await new TopUpHandler(_db).Handle(
            new TopUpRequest
            {
                CustomerId = customer.Value.Id,
                Amount = 10000m,
                Reference = "first-deployment-smoke-credit",
            },
            CancellationToken.None);
        Assert.True(topUp.IsSuccess, topUp.Error?.Message);

        Result<IssueApiKeyResponse> apiKey = await new IssueApiKeyHandler(_db).Handle(
            new IssueApiKeyRequest
            {
                CustomerId = customer.Value.Id,
                Name = "first-deployment-smoke-key",
            },
            CancellationToken.None);
        Assert.True(apiKey.IsSuccess, apiKey.Error?.Message);
        Assert.False(string.IsNullOrWhiteSpace(apiKey.Value.Key));

        Result<CreateProviderAccountResponse> providerAccount = await new CreateProviderAccountHandler(
            _db,
            _secretProtector).Handle(
                new CreateProviderAccountRequest
                {
                    ProviderCode = "magfa",
                    DisplayName = "First Deployment Magfa Account",
                    AuthType = ProviderAccountAuthTypes.UsernamePasswordDomain,
                    Settings = new Dictionary<string, string>
                    {
                        ["username"] = "magfa-user",
                        ["domain"] = "magfa-domain",
                    },
                    Secret = "magfa-password",
                    IsActive = true,
                },
                CancellationToken.None);
        Assert.True(providerAccount.IsSuccess, providerAccount.Error?.Message);

        Result<CreateSenderLineResponse> senderLine = await new CreateSenderLineHandler(_db).Handle(
            new CreateSenderLineRequest
            {
                ProviderId = 1,
                LineNumber = "300099990001",
                IsSharedLine = true,
                ProviderAccountId = providerAccount.Value.Id,
            },
            CancellationToken.None);
        Assert.True(senderLine.IsSuccess, senderLine.Error?.Message);

        int tariffId = await CreateTariffAsync();

        Result<SendMessagesResponse> send = await new SendMessagesHandler(_db, TimeProvider.System).Handle(
            new SendMessagesRequest
            {
                SenderLine = "300099990001",
                MessageTypeId = 1,
                ClientBatchId = "first-deployment-smoke-001",
                Messages =
                [
                    new SendMessageItem
                    {
                        Recipient = "989120000001",
                        Text = "Hello",
                    },
                ],
            },
            new ApiKeyIdentity(apiKey.Value.Id, customer.Value.Id, apiKey.Value.KeyPrefix),
            CancellationToken.None);
        Assert.True(send.IsSuccess, send.Error?.Message);
        Assert.Equal(1, send.Value.AcceptedCount);

        MessageDispatcher dispatcher = new MessageDispatcher(
            _db,
            new SmsProviderRegistry([new SmokeSmsProvider()]),
            new DispatchOptions(),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));

        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);

        SmokeWorkflowState state = await connection.QuerySingleAsync<SmokeWorkflowState>(
            """
            SELECT
                b.Status AS BatchStatus,
                b.MessageCount,
                b.TotalCost,
                m.Status AS MessageStatus,
                m.DeliveryStatus,
                m.ProviderMessageId,
                cb.Balance,
                pa.SecretEncrypted,
                m.TariffId
            FROM dbo.MessageBatch b
            JOIN dbo.Message m ON m.MessageBatchId = b.Id
            JOIN dbo.CustomerBalance cb ON cb.CustomerId = b.CustomerId
            JOIN dbo.SenderLine sl ON sl.Id = b.SenderLineId
            JOIN dbo.ProviderAccount pa ON pa.Id = sl.ProviderAccountId
            WHERE b.Id = @BatchId;
            """,
            new { BatchId = send.Value.BatchId });

        Assert.Equal((byte)BatchStatus.DispatchCompleted, state.BatchStatus);
        Assert.Equal(1, state.MessageCount);
        Assert.Equal(1000m, state.TotalCost);
        Assert.Equal((byte)SendStatus.Submitted, state.MessageStatus);
        Assert.Equal((byte)DeliveryStatus.Pending, state.DeliveryStatus);
        Assert.False(string.IsNullOrWhiteSpace(state.ProviderMessageId));
        Assert.Equal(9000m, state.Balance);
        Assert.Equal(tariffId, state.TariffId);
        Assert.NotNull(state.SecretEncrypted);
        Assert.NotEqual("magfa-password", Convert.ToBase64String(state.SecretEncrypted));
    }

    private async Task<int> CreateTariffAsync()
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync(CancellationToken.None);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Tariff
                (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive)
            VALUES
                (1, NULL, 0, '2025-01-01T00:00:00', NULL, 'IRR', 1);

            DECLARE @TariffId INT = CONVERT(INT, SCOPE_IDENTITY());

            INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
            VALUES (@TariffId, 0, NULL, 1000.0000);

            SELECT @TariffId;
            """,
            cancellationToken: CancellationToken.None));
    }

    private sealed record SmokeWorkflowState(
        byte BatchStatus,
        int MessageCount,
        decimal TotalCost,
        byte MessageStatus,
        byte DeliveryStatus,
        string? ProviderMessageId,
        decimal Balance,
        byte[] SecretEncrypted,
        int TariffId);

    private sealed class SmokeSmsProvider : ISmsProvider
    {
        public string Name => "magfa";

        public int MaxBatchSize => 1000;

        public bool SupportsIdempotentResend => true;

        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(
            IReadOnlyList<ProviderSendRequest> requests,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Result<ProviderDispatchResult>> results = requests
                .Select(request => Result.Success(ProviderDispatchResult.Accepted($"smoke-provider-{request.MessageId}")))
                .ToList();

            return Task.FromResult(Result.Success(results));
        }

        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) =>
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
