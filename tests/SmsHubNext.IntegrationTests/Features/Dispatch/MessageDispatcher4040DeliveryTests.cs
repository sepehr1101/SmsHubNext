using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.DeliveryReports;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.Providers.Provider4040;
using SmsHubNext.Shared.Enums;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

public sealed partial class MessageDispatcherTests
{
    [Theory]
    [InlineData(true, 100, 2)]
    [InlineData(true, 40, 3)]
    [InlineData(false, 100, 105)]
    public async Task Esb_delivery_poll_projects_every_chunk_once_in_batch_and_legacy_modes(
        bool batch, int size, int calls)
    {
        long batchId = await PrepareEsbDeliveryBatch(105);
        using DeliveryEsbGateway gateway = new();
        using HttpClient client = new(gateway);
        EsbOptions options = DeliveryEsbOptions(batch, size);
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        DeliveryReportPoller poller = EsbDeliveryPoller(TestEsbProvider(client, options, tokens));

        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));
        Assert.False(await poller.PollNextBatchAsync(CancellationToken.None));

        await AssertEsbDeliveryCounts(batchId, delivered: 105, pending: 0);
        Assert.Equal(calls, gateway.Queries.Count);
        Assert.All(gateway.Queries, ids => Assert.InRange(ids.Length, 1, batch ? size : 1));
        string[] requested = gateway.Queries.SelectMany(ids => ids).ToArray();
        Assert.Equal(105, requested.Length);
        Assert.Equal(105, requested.Distinct().Count());
        Assert.Equal(1, gateway.Logins);
    }

    [Fact]
    public async Task Esb_delivery_missing_and_intermediate_results_remain_queued_until_a_later_poll()
    {
        long batchId = await PrepareEsbDeliveryBatch(5);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        string[] ids = (await connection.QueryAsync<string>(
            "SELECT ProviderMessageId FROM dbo.Message WHERE MessageBatchId = @batchId ORDER BY Id", new { batchId })).ToArray();
        using DeliveryEsbGateway gateway = new() { MissingId = ids[0], PendingId = ids[1] };
        using HttpClient client = new(gateway);
        EsbOptions options = DeliveryEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        DeliveryReportPoller poller = EsbDeliveryPoller(TestEsbProvider(client, options, tokens));

        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));
        await AssertEsbDeliveryCounts(batchId, delivered: 3, pending: 2);
        Assert.Equal(ids.Take(2), await connection.QueryAsync<string>(
            "SELECT ProviderMessageId FROM dbo.DeliveryReportPoll ORDER BY MessageId"));
        Assert.False(await poller.PollNextBatchAsync(CancellationToken.None));

        gateway.MissingId = null;
        gateway.PendingId = null;
        await MakeEsbDeliveryPollsDue();
        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));
        await AssertEsbDeliveryCounts(batchId, delivered: 5, pending: 0);
        Assert.Equal(2, gateway.Queries.Count);
        Assert.Equal(2, gateway.Queries[1].Length);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("malformed")]
    [InlineData("entry")]
    [InlineData("disconnect")]
    [InlineData("timeout")]
    public async Task Esb_delivery_failed_second_chunk_preserves_history_and_recovers_after_legacy_rollback(string fault)
    {
        long batchId = await PrepareEsbDeliveryBatch(105);
        using DeliveryEsbGateway gateway = new() { Fault = fault };
        using HttpClient client = new(gateway);
        EsbOptions options = DeliveryEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        DeliveryReportPoller poller = EsbDeliveryPoller(TestEsbProvider(client, options, tokens));

        Assert.True(await poller.PollNextBatchAsync(CancellationToken.None));
        Assert.Equal(new[] { 100, 5 }, gateway.Queries.Select(ids => ids.Length));
        await AssertEsbDeliveryCounts(batchId, delivered: 0, pending: 105);
        Assert.False(await poller.PollNextBatchAsync(CancellationToken.None));

        // Simulate a host restart with the rollback setting; reuse the durable polling queue.
        EsbOptions legacyOptions = DeliveryEsbOptions(batch: false);
        DeliveryReportPoller restarted = EsbDeliveryPoller(TestEsbProvider(client, legacyOptions, tokens));
        await MakeEsbDeliveryPollsDue();
        Assert.True(await restarted.PollNextBatchAsync(CancellationToken.None));
        Assert.False(await restarted.PollNextBatchAsync(CancellationToken.None));
        await AssertEsbDeliveryCounts(batchId, delivered: 105, pending: 0);
        Assert.Equal(107, gateway.Queries.Count);
        Assert.All(gateway.Queries.Skip(2), ids => Assert.Single(ids));
    }

    private async Task<long> PrepareEsbDeliveryBatch(int count)
    {
        (long batchId, short customerId) = await PrepareEsbBatch(count);
        using DeduplicatingEsbGateway gateway = new();
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Assert.True(await EsbDispatcher(TestEsbProvider(client, options, tokens)).DispatchNextBatchAsync(CancellationToken.None));
        await AssertEsbCompleted(batchId, customerId, count);
        return batchId;
    }

    private static EsbOptions DeliveryEsbOptions(bool batch = true, int size = 100) => new()
    {
        Enabled = true, Username = "test", Password = "test",
        UseBatchDeliveryQueries = batch, DeliveryQueryBatchSize = size,
        TerminalDeliveryStatuses = new() { [1] = DeliveryReportStatus.Delivered },
    };

    private DeliveryReportPoller EsbDeliveryPoller(Provider4040SmsProvider provider) => new(
        _db, new SmsProviderRegistry([provider]),
        new DeliveryReportPollOptions { BatchSize = 200, RetryDelay = TimeSpan.FromHours(1) },
        TimeProvider.System, NullLogger<DeliveryReportPoller>.Instance);

    private async Task MakeEsbDeliveryPollsDue()
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE dbo.DeliveryReportPoll SET NextPollAtUtc = DATEADD(minute, -1, SYSUTCDATETIME())");
    }

    private async Task AssertEsbDeliveryCounts(long batchId, int delivered, int pending)
    {
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        Assert.Equal(delivered, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Message WHERE MessageBatchId = @batchId AND DeliveryStatus = @status AND DeliveredAtUtc IS NOT NULL",
            new { batchId, status = (byte)DeliveryStatus.Delivered }));
        Assert.Equal(pending, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Message WHERE MessageBatchId = @batchId AND DeliveryStatus = @status AND DeliveredAtUtc IS NULL",
            new { batchId, status = (byte)DeliveryStatus.Pending }));
        Assert.Equal(pending, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReportPoll p JOIN dbo.Message m ON m.Id = p.MessageId WHERE m.MessageBatchId = @batchId", new { batchId }));
        Assert.Equal(delivered, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReport d JOIN dbo.Message m ON m.Id = d.MessageId WHERE m.MessageBatchId = @batchId AND d.NormalizedStatus = @status",
            new { batchId, status = (byte)DeliveryReportStatus.Delivered }));
        Assert.Equal(delivered, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DeliveryReport d JOIN dbo.Message m ON m.Id = d.MessageId WHERE m.MessageBatchId = @batchId", new { batchId }));
    }

    private sealed class DeliveryEsbGateway : HttpMessageHandler
    {
        public string? Fault { get; init; }
        public string? MissingId { get; set; }
        public string? PendingId { get; set; }
        public int Logins { get; private set; }
        public List<string[]> Queries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                Logins++;
                return Json("""{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""");
            }
            Assert.Equal("/Nww-CheckDeliverySms/1.0", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            Assert.Equal("Basic bugfix", Assert.Single(request.Headers.GetValues("basicAuthorization")));
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string[] ids = body.RootElement.GetProperty("_body").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Queries.Add(ids);
            if (Queries.Count == 2)
            {
                switch (Fault)
                {
                    case "http": return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    case "malformed": return Json("not-json");
                    case "entry": return Json(JsonSerializer.Serialize(new
                    {
                        result = new { data = new { status = 0, dlrs = new Dictionary<string, object> { [ids[0]] = new { } } }, status = new { statusCode = 200 } },
                        status = new { statusCode = 200 },
                    }));
                    case "disconnect": throw new HttpRequestException("Delivery connection lost");
                    case "timeout": throw new TaskCanceledException("Delivery query timed out");
                }
            }
            // Reverse response order, include an unrelated ID, and never use uid for correlation.
            return Json(JsonSerializer.Serialize(new
            {
                result = new
                {
                    data = new
                    {
                        status = 0,
                        dlrs = ids.Reverse().Where(id => id != MissingId).Append("unrequested-id")
                            .ToDictionary(id => id, id => new { dlr = id == PendingId ? 8 : 1, uid = "unrelated" }),
                    },
                    status = new { statusCode = 200 },
                },
                status = new { statusCode = 200 },
            }));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
