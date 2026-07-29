using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.SmsIr;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Providers;

/// <summary>
/// Exercises the sms.ir provider seam against the official REST envelope through WireMock.
/// No database, provider credential, or Docker daemon is required.
/// </summary>
public sealed class SmsIrSmsProviderTests : IDisposable
{
    private const string SendPath = "/v1/send/likeToLike";

    private readonly WireMockServer _smsIr = WireMockServer.Start();

    public void Dispose() => _smsIr.Stop();

    [Fact]
    public void Provider_metadata_matches_smsir_safety_contract()
    {
        SmsIrSmsProvider provider = NewProvider();

        Assert.Equal(ProviderCodes.SmsIr, provider.Name);
        Assert.Equal(SmsIrOptions.MaxMessagesPerRequest, provider.MaxBatchSize);
        Assert.False(provider.SupportsIdempotentResend);
    }

    [Fact]
    public async Task Empty_batch_returns_without_an_http_call()
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result =
            await NewProvider().SendBatchAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value);
        Assert.Empty(_smsIr.LogEntries);
    }

    [Fact]
    public async Task Batch_send_uses_like_to_like_api_key_and_parallel_arrays()
    {
        StubSend(200, SuccessSend("[9001,9002]"));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "30004505000017", "09120000001", "متن اول"),
                new ProviderSendRequest(222, "30004505000017", "09120000002", "متن دوم"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal("9001", batch.Value[0].Value.ProviderMessageId);
        Assert.Equal("9002", batch.Value[1].Value.ProviderMessageId);

        WireMock.Logging.ILogEntry entry = Assert.Single(_smsIr.LogEntries);
        Assert.Equal(SendPath, entry.RequestMessage.Path);
        Assert.Equal("test-key", Assert.Single(entry.RequestMessage.Headers!["X-API-KEY"]));

        using JsonDocument json = JsonDocument.Parse(entry.RequestMessage.Body!);
        JsonElement root = json.RootElement;
        Assert.Equal(30004505000017, root.GetProperty("lineNumber").GetInt64());
        Assert.Equal(
            ["متن اول", "متن دوم"],
            root.GetProperty("messageTexts").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(
            ["09120000001", "09120000002"],
            root.GetProperty("mobiles").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sendDateTime").ValueKind);
    }

    [Fact]
    public async Task Different_sender_lines_are_sent_as_separate_requests()
    {
        StubSend(200, SuccessSend("[9001]"));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider(
            senderLines: ["30004505000017", "30004505000018"]).SendBatchAsync(
            [
                new ProviderSendRequest(1, "30004505000017", "09120000001", "A"),
                new ProviderSendRequest(2, "30004505000018", "09120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(2, _smsIr.LogEntries.Count());

        long[] lines = _smsIr.LogEntries
            .Select(entry =>
            {
                using JsonDocument json = JsonDocument.Parse(entry.RequestMessage.Body!);
                return json.RootElement.GetProperty("lineNumber").GetInt64();
            })
            .Order()
            .ToArray();
        Assert.Equal([30004505000017, 30004505000018], lines);
    }

    [Fact]
    public async Task Send_chunks_direct_calls_at_the_configured_provider_limit()
    {
        StubSend(200, SuccessSend("[" + string.Join(",", Enumerable.Repeat("9001", 100)) + "]"));

        ProviderSendRequest[] requests = Enumerable.Range(1, 101)
            .Select(index => new ProviderSendRequest(index, "30004505000017", $"0912{index:D7}", $"M{index}"))
            .ToArray();

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch =
            await NewProvider().SendBatchAsync(requests, CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(2, _smsIr.LogEntries.Count());
    }

    [Fact]
    public async Task Zero_and_null_message_ids_are_positionally_rejected()
    {
        StubSend(200, SuccessSend("[9001,0,null]"));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(1, "30004505000017", "09120000001", "A"),
                new ProviderSendRequest(2, "30004505000017", "09120000002", "B"),
                new ProviderSendRequest(3, "30004505000017", "bad-mobile", "C"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Accepted, batch.Value[0].Value.Status);
        Assert.Equal(ProviderDispatchStatus.Rejected, batch.Value[1].Value.Status);
        Assert.Equal(SmsIrStatusCodes.Blacklisted, batch.Value[1].Value.ProviderResultCode);
        Assert.Equal(ProviderDispatchStatus.Rejected, batch.Value[2].Value.Status);
        Assert.Null(batch.Value[2].Value.ProviderResultCode);
    }

    [Fact]
    public async Task Missing_positional_result_is_an_unknown_per_message_outcome()
    {
        StubSend(200, SuccessSend("[9001]"));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(1, "30004505000017", "09120000001", "A"),
                new ProviderSendRequest(2, "30004505000017", "09120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.True(batch.Value[0].IsSuccess);
        Assert.True(batch.Value[1].IsFailure);
        Assert.Equal("smsir.missing_result", batch.Value[1].Error!.Code);
    }

    [Fact]
    public async Task Unknown_sender_line_is_an_inner_error_and_does_not_call_smsir()
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [new ProviderSendRequest(1, "30009999", "09120000001", "A")],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Result<ProviderDispatchResult> result = Assert.Single(batch.Value);
        Assert.True(result.IsFailure);
        Assert.Equal("smsir.unknown_sender_line", result.Error!.Code);
        Assert.Empty(_smsIr.LogEntries);
    }

    [Fact]
    public async Task Non_numeric_configured_sender_line_is_an_inner_error()
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider(
            senderLines: ["not-numeric"]).SendBatchAsync(
            [new ProviderSendRequest(1, "not-numeric", "09120000001", "A")],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal("smsir.invalid_sender_line", Assert.Single(batch.Value).Error!.Code);
        Assert.Empty(_smsIr.LogEntries);
    }

    [Fact]
    public async Task Request_level_insufficient_credit_holds_the_batch()
    {
        StubSend(400, Failure(102));

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.InsufficientCredit, result.Value.Status);
        Assert.Equal(102, result.Value.ProviderResultCode);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task Authentication_and_account_statuses_are_definitely_not_submitted(int status)
    {
        StubSend(400, Failure(status));

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.DefinitelyNotSubmitted, result.Value.Status);
        Assert.Equal(status, result.Value.ProviderResultCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public async Task Explicit_transient_statuses_are_retryable_without_confirmation(int status)
    {
        StubSend(status == 20 ? 429 : 400, Failure(status));

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.RetryableNotSubmitted, result.Value.Status);
        Assert.Equal(status, result.Value.ProviderResultCode);
    }

    [Theory]
    [MemberData(nameof(RejectedRequestStatuses))]
    public async Task Validation_and_configuration_statuses_reject_and_refund(int status)
    {
        StubSend(400, Failure(status));

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Rejected, result.Value.Status);
        Assert.Equal(status, result.Value.ProviderResultCode);
    }

    [Fact]
    public async Task Undocumented_request_status_is_kept_as_an_unknown_provider_failure()
    {
        StubSend(400, Failure(999));

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsFailure);
        Assert.Equal("smsir.request_status", result.Error!.Code);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Bare_http_authentication_failure_is_definitely_not_submitted(int status)
    {
        StubSend(status, "authentication failed", "text/plain");

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.DefinitelyNotSubmitted, result.Value.Status);
        Assert.Equal(status, result.Value.ProviderResultCode);
    }

    [Fact]
    public async Task Bare_http_rate_limit_is_retryable()
    {
        StubSend(429, "slow down", "text/plain");

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.RetryableNotSubmitted, result.Value.Status);
        Assert.Equal(429, result.Value.ProviderResultCode);
    }

    [Fact]
    public async Task Bare_http_bad_request_is_rejected()
    {
        StubSend(400, "bad request", "text/plain");

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Rejected, result.Value.Status);
        Assert.Equal(400, result.Value.ProviderResultCode);
    }

    [Fact]
    public async Task Http_5xx_is_an_unknown_transport_outcome()
    {
        StubSend(500, "upstream failure", "text/plain");

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsFailure);
        Assert.Equal("smsir.http_status", result.Error!.Code);
    }

    [Fact]
    public async Task Malformed_success_response_is_an_unknown_outcome()
    {
        StubSend(200, "{broken");

        Result<ProviderDispatchResult> result = await SendOne();

        Assert.True(result.IsFailure);
        Assert.Equal("smsir.bad_json", result.Error!.Code);
    }

    [Fact]
    public async Task Resolve_returns_null_without_calling_smsir()
    {
        Result<string?> result =
            await NewProvider().ResolveSubmittedMessageIdAsync(450, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Value);
        Assert.Empty(_smsIr.LogEntries);
    }

    [Fact]
    public async Task Delivery_states_map_to_normalized_terminal_or_in_flight_outcomes()
    {
        int?[] states = [1, 2, 3, 4, 5, 6, 7, null];
        for (int index = 0; index < states.Length; index++)
        {
            string id = (index + 1).ToString();
            string deliveryState = states[index]?.ToString() ?? "null";
            StubStatus(id, 200, $$"""
                {
                  "status": 1,
                  "message": "موفق",
                  "data": { "messageId": {{id}}, "deliveryState": {{deliveryState}} }
                }
                """);
        }

        string[] ids = Enumerable.Range(1, states.Length).Select(index => index.ToString()).ToArray();
        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(ids, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Dictionary<string, ProviderDeliveryReport> byId = result.Value.ToDictionary(report => report.ProviderMessageId);
        Assert.Equal(DeliveryReportStatus.Delivered, byId["1"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["2"].Status);
        Assert.Null(byId["3"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["4"].Status);
        Assert.Null(byId["5"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["6"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["7"].Status);
        Assert.Null(byId["8"].Status);
        Assert.Equal(0, byId["8"].RawStatusCode);
    }

    [Fact]
    public async Task Delivery_lookup_uses_message_path_and_api_key()
    {
        StubStatus("89545112", 200, """
            {
              "status": 1,
              "message": "موفق",
              "data": { "messageId": 89545112, "deliveryState": 1 }
            }
            """);

        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(["89545112"], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        WireMock.Logging.ILogEntry entry = Assert.Single(_smsIr.LogEntries);
        Assert.Equal("/v1/send/89545112", entry.RequestMessage.Path);
        Assert.Equal("test-key", Assert.Single(entry.RequestMessage.Headers!["X-API-KEY"]));
    }

    [Fact]
    public async Task Delivery_lookup_tries_the_next_account_when_message_is_not_found()
    {
        StubStatusForKey("77", "first-key", 400, Failure(111));
        StubStatusForKey("77", "second-key", 200, """
            {
              "status": 1,
              "message": "موفق",
              "data": { "messageId": 77, "deliveryState": 1 }
            }
            """);

        SmsIrAccount[] accounts =
        [
            new SmsIrAccount { ApiKey = "first-key", SenderLines = ["30001"] },
            new SmsIrAccount { ApiKey = "second-key", SenderLines = ["30002"] },
        ];
        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider(accounts: accounts).GetDeliveryReportsAsync(["77"], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(result.Value);
        Assert.Equal(2, _smsIr.LogEntries.Count());
    }

    [Fact]
    public async Task Latest_inbound_maps_numbers_body_timestamp_and_count()
    {
        StubInbound(200, """
            {
              "status": 1,
              "message": "موفق",
              "data": [
                {
                  "receiveReturnId": 123456789,
                  "messageText": "پاسخ",
                  "number": 30004505000017,
                  "mobile": 9120000001,
                  "receivedDateTime": 1800000001
                }
              ]
            }
            """);

        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(50, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        ProviderInboundMessage message = Assert.Single(result.Value);
        Assert.Equal("9120000001", message.SenderNumber);
        Assert.Equal("30004505000017", message.RecipientNumber);
        Assert.Equal("پاسخ", message.Body);
        Assert.Equal("1800000001", message.ProviderTimestamp);

        WireMock.Logging.ILogEntry entry = Assert.Single(_smsIr.LogEntries);
        Assert.Equal("/v1/receive/latest", entry.RequestMessage.Path);
        Assert.Equal("50", Assert.Single(entry.RequestMessage.Query!["count"]));
    }

    [Fact]
    public async Task Inbound_count_is_capped_at_the_official_limit()
    {
        StubInbound(200, """{ "status": 1, "message": "موفق", "data": [] }""");

        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(500, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        WireMock.Logging.ILogEntry entry = Assert.Single(_smsIr.LogEntries);
        Assert.Equal("100", Assert.Single(entry.RequestMessage.Query!["count"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_inbound_count_returns_without_destructive_pull(int maxCount)
    {
        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(maxCount, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value);
        Assert.Empty(_smsIr.LogEntries);
    }

    public static TheoryData<int> RejectedRequestStatuses => new TheoryData<int>
    {
        101, 103, 104, 105, 106, 107, 108, 109, 110,
        112, 113, 114, 115, 116, 117, 118, 119, 123,
    };

    private async Task<Result<ProviderDispatchResult>> SendOne()
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [new ProviderSendRequest(1, "30004505000017", "09120000001", "Hello")],
            CancellationToken.None);

        if (batch.IsFailure)
            return batch.Error!;
        return batch.Value[0];
    }

    private SmsIrSmsProvider NewProvider(
        IReadOnlyList<string>? senderLines = null,
        IReadOnlyList<SmsIrAccount>? accounts = null)
    {
        HttpClient httpClient = new HttpClient
        {
            BaseAddress = new Uri(_smsIr.Urls[0]),
        };
        SmsIrOptions options = new SmsIrOptions
        {
            BatchSize = SmsIrOptions.MaxMessagesPerRequest,
        };
        IReadOnlyList<SmsIrAccount> configuredAccounts = accounts ??
        [
            new SmsIrAccount
            {
                ApiKey = "test-key",
                SenderLines = senderLines ?? ["30004505000017"],
            },
        ];

        return new SmsIrSmsProvider(
            httpClient,
            new SmsIrAccountResolver(configuredAccounts),
            options,
            TimeProvider.System,
            NullLogger<SmsIrSmsProvider>.Instance);
    }

    private void StubSend(int statusCode, string body, string contentType = "application/json") =>
        _smsIr
            .Given(Request.Create().WithPath(SendPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", contentType)
                .WithBody(body));

    private void StubStatus(string messageId, int statusCode, string body) =>
        _smsIr
            .Given(Request.Create().WithPath($"/v1/send/{messageId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubStatusForKey(string messageId, string apiKey, int statusCode, string body) =>
        _smsIr
            .Given(Request.Create()
                .WithPath($"/v1/send/{messageId}")
                .WithHeader("X-API-KEY", apiKey)
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubInbound(int statusCode, string body) =>
        _smsIr
            .Given(Request.Create().WithPath("/v1/receive/latest").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private static string SuccessSend(string messageIds) =>
        $$"""
        {
          "status": 1,
          "message": "موفق",
          "data": {
            "packId": "2b99e63c-9bf8-4a21-9bfe-3f72dc1b46f1",
            "messageIds": {{messageIds}},
            "cost": 2.0
          }
        }
        """;

    private static string Failure(int status) =>
        $$"""{ "status": {{status}}, "message": "failure", "data": null }""";
}
