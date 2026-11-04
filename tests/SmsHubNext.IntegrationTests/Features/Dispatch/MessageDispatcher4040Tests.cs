using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Dispatch;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.Providers.Provider4040;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

public sealed partial class MessageDispatcherTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(850)]
    public async Task Esb_batches_preserve_every_uid_once_even_with_competing_workers(int count)
    {
        (long batchId, short customerId) = await PrepareEsbBatch(count);
        using DeduplicatingEsbGateway gateway = new();
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Provider4040SmsProvider provider = TestEsbProvider(client, options, tokens);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None)));

        int[] sizes = Enumerable.Range(0, (count + 99) / 100).Select(index => Math.Min(100, count - index * 100)).ToArray();
        Assert.Equal(sizes, gateway.RequestUids.Select(ids => ids.Length));
        Assert.Equal(count, gateway.RequestUids.SelectMany(ids => ids).Distinct().Count());
        Assert.Equal(count, gateway.AcceptedUids.Count);
        await AssertEsbCompleted(batchId, customerId, count);
        Assert.False(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(sizes.Length, gateway.RequestUids.Count);
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("missing-id")]
    [InlineData("duplicate-id")]
    public async Task Esb_lost_second_chunk_retries_only_uncertain_uids_without_duplicate_acceptance(string fault)
    {
        (long batchId, short customerId) = await PrepareEsbBatch(250);
        using DeduplicatingEsbGateway gateway = new() { Fault = fault };
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Provider4040SmsProvider provider = TestEsbProvider(client, options, tokens);
        Assert.True(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(new[] { 100, 100 }, gateway.RequestUids.Select(ids => ids.Length));
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(100, await CountEsbMessages(connection, batchId, SendStatus.Submitted));
        Assert.Equal(100, await CountEsbMessages(connection, batchId, SendStatus.AwaitingConfirmation));
        Assert.Equal(50, await CountEsbMessages(connection, batchId, SendStatus.Queued));

        // Recreate the dispatcher AND provider: safety must come from persisted message IDs/counters.
        Provider4040SmsProvider restartedProvider = TestEsbProvider(client, options, tokens);
        Assert.True(await EsbDispatcher(restartedProvider).DispatchNextBatchAsync(CancellationToken.None));

        Assert.Equal(new[] { 100, 100, 100, 50 }, gateway.RequestUids.Select(ids => ids.Length));
        Assert.Equal(gateway.RequestUids[1], gateway.RequestUids[2]);
        Assert.Empty(gateway.RequestUids[0].Intersect(gateway.RequestUids.Skip(1).SelectMany(ids => ids)));
        Assert.Equal(250, gateway.AcceptedUids.Count);
        Assert.Equal(250, gateway.RequestUids.SelectMany(ids => ids).Distinct().Count());
        Assert.All(gateway.RequestUids.SelectMany(ids => ids).GroupBy(id => id), group =>
            Assert.Equal(gateway.RequestUids[1].Contains(group.Key) ? 2 : 1, group.Count()));
        await AssertEsbCompleted(batchId, customerId, 250);
        Assert.False(await EsbDispatcher(restartedProvider).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(4, gateway.RequestUids.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Esb_uncertain_chunk_stops_at_retry_budget_or_uid_expiry(bool expireWindow)
    {
        (long batchId, short customerId) = await PrepareEsbBatch(250);
        using DeduplicatingEsbGateway gateway = new() { Fault = "disconnect", FailEveryRequestAfterFirst = true };
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Provider4040SmsProvider provider = TestEsbProvider(client, options, tokens);
        Assert.True(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        if (expireWindow)
        {
            // Exactly 24 hours is outside the guarantee. Do not fabricate new first-attempt times.
            await connection.ExecuteAsync("""
                UPDATE dbo.Message SET FirstSendAttemptAtUtc = DATEADD(hour, -24, SYSUTCDATETIME())
                WHERE MessageBatchId = @batchId AND Status = @status;
                """, new { batchId, status = (byte)SendStatus.AwaitingConfirmation });
        }
        else
        {
            for (int retry = 0; retry < 3; retry++)
                Assert.True(await EsbDispatcher(TestEsbProvider(client, options, tokens)).DispatchNextBatchAsync(CancellationToken.None));
        }
        Assert.True(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.Held, batch.Value.Status);
        Assert.Equal(BatchStatusReason.ManualReviewRequired, batch.Value.StatusReason);
        Assert.Equal(expireWindow ? 2 : 5, gateway.RequestUids.Count);
        Assert.All(gateway.RequestUids.Skip(1), ids => Assert.Equal(gateway.RequestUids[1], ids));
        Assert.Equal(200, gateway.AcceptedUids.Count);
        Assert.Equal(100, await CountEsbMessages(connection, batchId, SendStatus.Submitted));
        Assert.Equal(100, await CountEsbMessages(connection, batchId, SendStatus.AwaitingConfirmation));
        Assert.Equal(50, await CountEsbMessages(connection, batchId, SendStatus.Queued));
        Assert.Equal(expireWindow ? 1 : 4, await connection.ExecuteScalarAsync<int>(
            "SELECT MAX(SendAttemptCount) FROM dbo.Message WHERE MessageBatchId = @batchId", new { batchId }));
        Assert.Equal(-240000m, await BalanceAsync(customerId));
        Assert.False(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(expireWindow ? 2 : 5, gateway.RequestUids.Count);
    }

    [Fact]
    public async Task Esb_shutdown_after_second_chunk_acceptance_waits_for_lease_then_reuses_original_uids()
    {
        (long batchId, short customerId) = await PrepareEsbBatch(250);
        using CancellationTokenSource shutdown = new();
        using DeduplicatingEsbGateway gateway = new() { Fault = "shutdown", Shutdown = shutdown };
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Provider4040SmsProvider provider = TestEsbProvider(client, options, tokens);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EsbDispatcher(provider).DispatchNextBatchAsync(shutdown.Token));
        Assert.Equal(2, gateway.RequestUids.Count);
        Assert.False(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(2, gateway.RequestUids.Count);

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(100, await CountEsbMessages(connection, batchId, SendStatus.AwaitingConfirmation));
        await connection.ExecuteAsync("""
            UPDATE dbo.MessageBatch SET DispatchLeaseExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME())
            WHERE Id = @batchId;
            """, new { batchId });
        Assert.True(await EsbDispatcher(TestEsbProvider(client, options, tokens)).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(new[] { 100, 100, 100, 50 }, gateway.RequestUids.Select(ids => ids.Length));
        Assert.Equal(gateway.RequestUids[1], gateway.RequestUids[2]);
        Assert.Empty(gateway.RequestUids[0].Intersect(gateway.RequestUids.Skip(1).SelectMany(ids => ids)));
        Assert.Equal(250, gateway.AcceptedUids.Count);
        await AssertEsbCompleted(batchId, customerId, 250);
        Assert.False(await EsbDispatcher(provider).DispatchNextBatchAsync(CancellationToken.None));
        Assert.Equal(4, gateway.RequestUids.Count);
    }

    private async Task<(long BatchId, short CustomerId)> PrepareEsbBatch(int count)
    {
        (long batchId, short customerId) = await SendBatchAsync(count);
        // Isolated fixture uses the seeded tariff/line; switch its routing to the real 4040 adapter.
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.Provider SET Code = '4040' WHERE Id = 1; UPDATE dbo.SenderLine SET UseEsb = 1 WHERE LineNumber = '30001234';");
        return (batchId, customerId);
    }

    private static EsbOptions TestEsbOptions() => new() { Enabled = true, Username = "test", Password = "test" };
    private static Provider4040SmsProvider TestEsbProvider(HttpClient client, EsbOptions options, EsbAccessTokenProvider tokens) =>
        new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string> { "30001234" }));

    private MessageDispatcher EsbDispatcher(ISmsProvider provider) => new(_db, new SmsProviderRegistry([provider]),
        new DispatchOptions { AwaitingConfirmationRetryDelay = TimeSpan.Zero }, TimeProvider.System,
        NullLogger<MessageDispatcher>.Instance);

    private async Task AssertEsbCompleted(long batchId, short customerId, int count)
    {
        Result<Batch> batch = await new GetBatchHandler(_db).Handle(batchId, CancellationToken.None);
        Assert.Equal(BatchStatus.DispatchCompleted, batch.Value.Status);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        List<EsbStoredMessage> messages = (await connection.QueryAsync<EsbStoredMessage>(
            "SELECT Id, Status, ProviderMessageId FROM dbo.Message WHERE MessageBatchId = @batchId ORDER BY Id", new { batchId })).ToList();
        Assert.Equal(count, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.Equal((byte)SendStatus.Submitted, message.Status);
            Assert.Equal($"sms-{message.Id}", message.ProviderMessageId);
        });
        Assert.Equal(10000m - count * 1000m, await BalanceAsync(customerId));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE MessageBatchId = @batchId AND Type = 2", new { batchId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.BalanceTransaction WHERE MessageBatchId = @batchId AND Type = 3", new { batchId }));
        Assert.Equal(count, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DispatchAcceptance a JOIN dbo.Message m ON m.Id = a.Id WHERE m.MessageBatchId = @batchId", new { batchId }));
        Assert.Equal(count, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReportPoll p JOIN dbo.Message m ON m.Id = p.MessageId WHERE m.MessageBatchId = @batchId", new { batchId }));
    }

    private static Task<int> CountEsbMessages(SqlConnection connection, long batchId, SendStatus status) =>
        connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Message WHERE MessageBatchId = @batchId AND Status = @status",
            new { batchId, status = (byte)status });

    private sealed record EsbStoredMessage(long Id, byte Status, string ProviderMessageId);
    private sealed class EsbTestClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    // No network or real credentials: emulate upstream UID deduplication even when its response is lost.
    private sealed class DeduplicatingEsbGateway : HttpMessageHandler
    {
        public string? Fault { get; init; }
        public bool FailEveryRequestAfterFirst { get; init; }
        public CancellationTokenSource? Shutdown { get; init; }
        public List<string[]> RequestUids { get; } = [];
        public HashSet<string> AcceptedUids { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Json("""{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""");
            Assert.Equal("/Nww-SendSms/1.0", request.RequestUri.AbsolutePath);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string[] uids = body.RootElement.GetProperty("uids").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.InRange(uids.Length, 1, 100);
            Assert.Equal(uids.Length, uids.Distinct().Count());
            foreach (string field in new[] { "senders", "messages", "recipients" })
                Assert.Equal(uids.Length, body.RootElement.GetProperty(field).GetArrayLength());
            lock (RequestUids)
            {
                RequestUids.Add(uids);
                foreach (string uid in uids)
                {
                    Assert.True(long.TryParse(uid, NumberStyles.None, CultureInfo.InvariantCulture, out _));
                    AcceptedUids.Add(uid);
                }
                string[] ids = uids.Select(uid => $"sms-{uid}").ToArray();
                if (RequestUids.Count == 2 || (FailEveryRequestAfterFirst && RequestUids.Count > 2))
                {
                    switch (Fault)
                    {
                        case "shutdown":
                            Shutdown!.Cancel();
                            throw new OperationCanceledException(cancellationToken);
                        case "disconnect": throw new HttpRequestException("Response lost after acceptance");
                        case "timeout": throw new TaskCanceledException("Response timed out after acceptance");
                        case "malformed": return Json("not-json");
                        case "missing-id": ids = ids[..^1]; break;
                        case "duplicate-id": ids[1] = ids[0]; break;
                    }
                }
                return Json(JsonSerializer.Serialize(new
                {
                    result = new { data = new { status = 0, smsids = ids }, status = new { statusCode = 200 } },
                    status = new { statusCode = 200 },
                }));
            }
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
