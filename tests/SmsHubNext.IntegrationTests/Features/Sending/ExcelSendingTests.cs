using Dapper;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using MiniExcelLibs;
using SmsHubNext.Features.ApiKeys;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Billing;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.ReferenceData.Customers;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Sending.Ui;
using SmsHubNext.IntegrationTests.Shared;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Sending;

// Isolated SQL container + in-memory provider only. Never start the application host,
// register real provider clients, or read local application settings/credentials.
public sealed class ExcelSendingTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();
    private readonly RecordingProvider _provider = new();
    private Db _db = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        string connectionString = _sqlServer.GetConnectionString();
        DatabaseUpgradeResult migration = new DatabaseMigrator(connectionString).Migrate();
        Assert.True(migration.Successful, migration.Error?.Message);
        _db = new Db(connectionString);
        await ProviderAccountTestData.AssignActiveMagfaAccountToDefaultTestLineAsync(_db);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            UPDATE dbo.Provider SET BaseUrl = 'http://127.0.0.1:1', FallbackBaseUrl = NULL;
            INSERT INTO dbo.Tariff (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, Currency, IsActive)
            VALUES (1, NULL, 1, '2025-01-01', 'IRR', 1);
            INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
            VALUES (CONVERT(INT, SCOPE_IDENTITY()), 0, NULL, 1000);
            """);
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Excel_messages_are_personalized_persisted_billed_and_dispatched_only_to_fake_provider(bool skipHeaders)
    {
        ApiKeyIdentity identity = await CreateIdentity(10000m);
        using MemoryStream workbook = Workbook(skipHeaders, [Row("علی", "00123", "09120000001"), Row("مریم", "00456", "09120000002")]);
        List<SendMessageDraft> drafts = ExcelMessageImport.Read(workbook, "مشترک [A]\nقبض [B]. [C]", "D", skipHeaders);
        Assert.Equal(2, drafts.Count);
        SendMessagesRequest request = Request(drafts);
        SendMessagesHandler handler = new(_db, TimeProvider.System);
        Result<SendMessagesResponse> result = await handler.Handle(request, identity, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.AcceptedCount);
        Assert.Empty(_provider.Requests);

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        List<StoredMessage> stored = (await connection.QueryAsync<StoredMessage>("""
            SELECT m.MobileNumber, b.Body, m.Status, m.TotalCost
            FROM dbo.Message m JOIN dbo.MessageBody b ON b.Id = m.Id
            WHERE m.MessageBatchId = @Id ORDER BY m.Id;
            """, new { Id = result.Value.BatchId })).ToList();
        Assert.Equal(new[] { "989120000001", "989120000002" }, stored.Select(message => message.MobileNumber));
        Assert.Equal(drafts.Select(draft => draft.Text), stored.Select(message => message.Body));
        Assert.All(stored, message => { Assert.Equal((byte)SendStatus.Queued, message.Status); Assert.Equal(1000m, message.TotalCost); });
        Assert.Equal(8000m, await Balance(connection, identity.CustomerId));

        Result<SendMessagesResponse> duplicate = await handler.Handle(request, identity, CancellationToken.None);
        Assert.True(duplicate.IsSuccess, duplicate.Error?.Message);
        Assert.True(duplicate.Value.IsDuplicate);
        Assert.Equal(result.Value.BatchId, duplicate.Value.BatchId);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId = @Id AND Type = 2", new { Id = identity.CustomerId }));

        MessageDispatcher dispatcher = Dispatcher();
        Assert.True(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));
        Assert.False(await dispatcher.DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(stored.Select(message => message.MobileNumber), _provider.Requests.Select(message => message.MobileNumber));
        Assert.Equal(drafts.Select(draft => draft.Text), _provider.Requests.Select(message => message.Body));
        Assert.Equal((byte)BatchStatus.DispatchCompleted, await connection.ExecuteScalarAsync<byte>("SELECT Status FROM dbo.MessageBatch WHERE Id = @Id", new { Id = result.Value.BatchId }));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message WHERE MessageBatchId = @Id AND Status = @Status AND ProviderMessageId LIKE 'fake-%'", new { Id = result.Value.BatchId, Status = (byte)SendStatus.Submitted }));
        Assert.Equal(8000m, await Balance(connection, identity.CustomerId));
    }

    [Fact]
    public async Task Insufficient_balance_rolls_back_excel_batch_and_never_calls_provider()
    {
        ApiKeyIdentity identity = await CreateIdentity(500m);
        using MemoryStream workbook = Workbook(true, [Row("علی", "00123", "09120000001")]);
        List<SendMessageDraft> drafts = ExcelMessageImport.Read(workbook, "مشترک [A] قبض [B]", "D", true);
        Result<SendMessagesResponse> result = await new SendMessagesHandler(_db, TimeProvider.System).Handle(Request(drafts), identity, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("sending.insufficient_balance", result.Error!.Code);
        await AssertNoSend(identity, 500m);
    }

    [Fact]
    public async Task Invalid_excel_row_never_creates_a_partial_batch_or_debit()
    {
        ApiKeyIdentity identity = await CreateIdentity(10000m);
        using MemoryStream workbook = Workbook(true, [Row("علی", "00123", "09120000001"), Row("مریم", "00456", "invalid")]);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ExcelMessageImport.Read(workbook, "مشترک [A] قبض [B]", "D", true));
        Assert.Contains("سطر 3", error.Message);
        await AssertNoSend(identity, 10000m);
    }

    private async Task AssertNoSend(ApiKeyIdentity identity, decimal expectedBalance)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MessageBatch WHERE CustomerId = @Id", new { Id = identity.CustomerId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message WHERE CustomerId = @Id", new { Id = identity.CustomerId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE CustomerId = @Id AND Type = 2", new { Id = identity.CustomerId }));
        Assert.Equal(expectedBalance, await Balance(connection, identity.CustomerId));
        Assert.False(await Dispatcher().DispatchNextBatchAsync(CancellationToken.None));
        Assert.Empty(_provider.Requests);
    }

    private MessageDispatcher Dispatcher() => new(_db, new SmsProviderRegistry([_provider]), new DispatchOptions(), TimeProvider.System, NullLogger<MessageDispatcher>.Instance);
    private static Task<decimal> Balance(SqlConnection connection, short customerId) => connection.ExecuteScalarAsync<decimal>("SELECT Balance FROM dbo.CustomerBalance WHERE CustomerId = @Id", new { Id = customerId });

    private async Task<ApiKeyIdentity> CreateIdentity(decimal credit)
    {
        Result<CreateCustomerResponse> customer = await new CreateCustomerHandler(_db).Handle(new CreateCustomerRequest { Name = "Excel test", Code = $"excel-{Guid.NewGuid():N}" }, CancellationToken.None);
        Assert.True(customer.IsSuccess, customer.Error?.Message);
        Result<TopUpResponse> topUp = await new TopUpHandler(_db).Handle(new TopUpRequest { CustomerId = customer.Value.Id, Amount = credit }, CancellationToken.None);
        Assert.True(topUp.IsSuccess, topUp.Error?.Message);
        Result<IssueApiKeyResponse> key = await new IssueApiKeyHandler(_db).Handle(new IssueApiKeyRequest { CustomerId = customer.Value.Id, Name = "Excel test" }, CancellationToken.None);
        Assert.True(key.IsSuccess, key.Error?.Message);
        return new ApiKeyIdentity(key.Value.Id, customer.Value.Id, key.Value.KeyPrefix);
    }

    private static SendMessagesRequest Request(List<SendMessageDraft> drafts) => new()
    {
        SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = $"excel-{Guid.NewGuid():N}",
        Messages = drafts.Select(draft => new SendMessageItem { Recipient = draft.Recipient, Text = draft.Text }).ToList(),
    };
    private static Dictionary<string, object> Row(string name, string bill, string mobile) => new() { ["Name"] = name, ["Bill"] = bill, ["Note"] = "یادآوری", ["Mobile"] = mobile };
    private static MemoryStream Workbook(bool headers, Dictionary<string, object>[] rows)
    {
        MemoryStream stream = new();
        MiniExcel.SaveAs(stream, rows, printHeader: headers, excelType: ExcelType.XLSX);
        stream.Position = 0;
        return stream;
    }

    private sealed class StoredMessage
    {
        public string MobileNumber { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public byte Status { get; set; }
        public decimal TotalCost { get; set; }
    }

    private sealed class RecordingProvider : ISmsProvider
    {
        public List<ProviderSendRequest> Requests { get; } = [];
        public string Name => "magfa";
        public int MaxBatchSize => 1000;
        public bool SupportsIdempotentResend => true;
        public Task<Result<IReadOnlyList<Result<ProviderDispatchResult>>>> SendBatchAsync(IReadOnlyList<ProviderSendRequest> requests, CancellationToken cancellationToken)
        {
            Requests.AddRange(requests);
            IReadOnlyList<Result<ProviderDispatchResult>> results = requests.Select(request => Result.Success(ProviderDispatchResult.Accepted($"fake-{request.MessageId}"))).ToList();
            return Task.FromResult(Result.Success(results));
        }
        public Task<Result<string?>> ResolveSubmittedMessageIdAsync(long messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ProviderDeliveryReport>>> GetDeliveryReportsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ProviderInboundMessage>>> FetchInboundMessagesAsync(int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
