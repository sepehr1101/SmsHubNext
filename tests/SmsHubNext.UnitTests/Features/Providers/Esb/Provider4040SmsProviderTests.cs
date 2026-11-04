using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.Providers.Provider4040;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Enums;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Providers.Esb;

public sealed class Provider4040SmsProviderTests
{
    [Theory]
    [InlineData(true, 100, 3)]
    [InlineData(true, 40, 6)]
    [InlineData(false, 100, 205)]
    public async Task Delivery_chunks_distinct_ids_and_can_revert_to_single_requests(bool batch, int batchSize, int calls)
    {
        string[] ids = Enumerable.Range(1, 205).Select(index => $"sms-{index}").ToArray();
        using GatewayHandler handler = new()
        {
            DeliveryResponse = JsonSerializer.Serialize(new
            {
                result = new
                {
                    data = new
                    {
                        status = 0,
                        dlrs = ids.Reverse().ToDictionary(id => id, id => new { dlr = 1, uid = "unrelated" }),
                    },
                    status = new { statusCode = 200 },
                },
                status = new { statusCode = 200 },
            }),
        };
        using HttpClient client = new(handler);
        EsbOptions options = new()
        {
            Enabled = true, Username = "client", Password = "secret",
            UseBatchDeliveryQueries = batch, DeliveryQueryBatchSize = batchSize,
            TerminalDeliveryStatuses = new() { [1] = DeliveryReportStatus.Delivered },
        };
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync(
            [.. ids, ids[0]], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ids, result.Value.Select(report => report.ProviderMessageId));
        Assert.All(result.Value, report => Assert.Equal(DeliveryReportStatus.Delivered, report.Status));
        Assert.Equal(calls, handler.DeliveryCalls);
        Assert.Equal(1, handler.Logins);
        List<string> queried = new();
        foreach (string payload in handler.DeliveryBodies)
        {
            using JsonDocument body = JsonDocument.Parse(payload);
            JsonElement array = body.RootElement.GetProperty("_body");
            Assert.InRange(array.GetArrayLength(), 1, batch ? batchSize : 1);
            queried.AddRange(array.EnumerateArray().Select(element => element.GetString()!));
        }
        Assert.Equal(ids, queried);
    }

    [Fact]
    public async Task Batch_delivery_preserves_missing_ids_and_ignores_unrequested_results()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync(
            ["missing-id", MessageId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.False(result.Value[0].HasProviderStatus);
        Assert.True(result.Value[1].HasProviderStatus);
        Assert.Equal(MessageId, result.Value[1].ProviderMessageId);
        Assert.Equal(1, handler.DeliveryCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_delivery_query_does_not_login(bool batch)
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = new() { UseBatchDeliveryQueries = batch };
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Equal(0, handler.Logins);
        Assert.Equal(0, handler.DeliveryCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Invalid_delivery_chunk_size_is_rejected(int size)
    {
        EsbOptions options = new() { Enabled = true, Username = "client", Password = "secret", DeliveryQueryBatchSize = size };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task Malformed_batch_entry_fails_the_query_without_returning_partial_results()
    {
        using GatewayHandler handler = new()
        {
            DeliveryResponse = """
                {"result":{"data":{"status":0,"dlrs":{"good":{"dlr":1},"bad":{}}},"status":{"statusCode":200}},"status":{"statusCode":200}}
                """,
        };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync(["good", "bad"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("4040.delivery_response", result.Error!.Code);
        Assert.Equal(1, handler.DeliveryCalls);
    }

    [Fact]
    public async Task A_hundred_messages_share_one_request_and_keep_payload_and_result_order()
    {
        string[] ids = Enumerable.Range(1, 100).Select(index => $"sms-{index}").ToArray();
        using GatewayHandler handler = new()
        {
            SendResponse = JsonSerializer.Serialize(new
            {
                result = new { data = new { status = 0, smsids = ids }, status = new { statusCode = 200 } },
                status = new { statusCode = 200 },
            }),
        };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        ProviderSendRequest[] requests = Enumerable.Range(1, 100)
            .Select(index => new ProviderSendRequest(index, "40401403", $"98912{index:D7}", $"پیام {index}"))
            .ToArray();

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(requests, CancellationToken.None);

        Assert.Equal(100, provider.MaxBatchSize);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, handler.Sends);
        Assert.Equal(ids, result.Value.Select(item => item.Value.ProviderMessageId));
        Assert.All(result.Value, item => Assert.Equal(ProviderDispatchStatus.Accepted, item.Value.Status));
        using JsonDocument body = JsonDocument.Parse(handler.SendBody!);
        foreach (string field in new[] { "senders", "messages", "recipients", "uids" })
            Assert.Equal(100, body.RootElement.GetProperty(field).GetArrayLength());
        for (int index = 0; index < requests.Length; index++)
        {
            Assert.Equal(requests[index].SenderLine, body.RootElement.GetProperty("senders")[index].GetString());
            Assert.Equal(requests[index].Body, body.RootElement.GetProperty("messages")[index].GetString());
            Assert.Equal("0" + requests[index].MobileNumber[2..], body.RootElement.GetProperty("recipients")[index].GetString());
            Assert.Equal((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), body.RootElement.GetProperty("uids")[index].GetString());
        }
    }

    [Fact]
    public async Task Oversized_upstream_request_is_blocked_before_login_or_send()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        ProviderSendRequest[] requests = Enumerable.Range(1, 101)
            .Select(index => new ProviderSendRequest(index, "40401403", "989135742556", "سلام"))
            .ToArray();

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(requests, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(101, result.Value.Count);
        Assert.All(result.Value, item => Assert.Equal(ProviderDispatchStatus.RetryableNotSubmitted, item.Value.Status));
        Assert.Equal(0, handler.Logins);
        Assert.Equal(0, handler.Sends);
    }

    [Fact]
    public async Task Delivery_reads_documented_string_codes_and_preserves_raw_provider_dates()
    {
        using GatewayHandler handler = new()
        {
            DeliveryResponse = """
                {"result":{"data":{"status":"0","dlrs":{"test-id":{"dlr":"10","DateSent":"1405/07/26 12:30","DateDlr":1700000000,"uid":"123"}}},"status":{"statusCode":200}},"status":{"statusCode":200}}
                """,
        };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));
        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync(["test-id"], CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        ProviderDeliveryReport report = Assert.Single(result.Value);
        Assert.True(report.HasProviderStatus);
        Assert.Equal(10, report.RawStatusCode);
        Assert.Null(report.Status);
        Assert.Equal("1405/07/26 12:30", report.ProviderSentAtRaw);
        Assert.Equal("1700000000", report.ProviderDlrAtRaw);
        Assert.Equal(0, handler.Sends);
    }

    private const string MessageId = "40401@1405@7@1510@Hawdy4AHJ@1@0@40401403@2556";
    private const string SuccessfulResponse =
        """
        {"result":{"data":{"status":0,"smsids":["40401@1405@7@1510@Hawdy4AHJ@1@0@40401403@2556"]},"status":{"statusCode":200}},"status":{"statusCode":200,"message":"OK"}}
        """;

    [Fact]
    public async Task Delivery_uses_same_cached_token_required_bugfix_header_and_full_sms_id()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = new()
        {
            Enabled = true, Username = "client", Password = "secret",
            TerminalDeliveryStatuses = new() { [1] = DeliveryReportStatus.Delivered },
        };
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        await provider.SendBatchAsync(Requests(), CancellationToken.None);

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        ProviderDeliveryReport report = Assert.Single(result.Value);
        Assert.Equal(MessageId, report.ProviderMessageId);
        Assert.Equal(DeliveryReportStatus.Delivered, report.Status);
        Assert.Equal(1, report.RawStatusCode);
        Assert.Equal(1, handler.Logins);
        Assert.Equal(1, handler.DeliveryCalls);
        using JsonDocument body = JsonDocument.Parse(handler.DeliveryBody!);
        Assert.Equal(MessageId, Assert.Single(body.RootElement.GetProperty("_body").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Unmapped_codes_remain_pending_instead_of_guessing_a_terminal_status()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value).Status);
        Assert.Equal(1, result.Value[0].RawStatusCode);
        Assert.Equal(0, handler.Sends);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"result\":{\"data\":{\"status\":1,\"dlrs\":{}},\"status\":{\"statusCode\":200}},\"status\":{\"statusCode\":200}}")]
    public async Task Invalid_delivery_response_returns_failure_for_later_poll_retry(string body)
    {
        using GatewayHandler handler = new() { DeliveryResponse = body };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.DeliveryCalls);
        Assert.Equal(0, handler.Sends);
    }

    [Fact]
    public async Task Missing_id_stays_pending_and_unrequested_ids_are_not_applied()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<ProviderDeliveryReport>> result = await provider.GetDeliveryReportsAsync(["another-id"], CancellationToken.None);

        ProviderDeliveryReport report = Assert.Single(result.Value);
        Assert.Equal("another-id", report.ProviderMessageId);
        Assert.Null(report.Status);
        Assert.False(report.HasProviderStatus);
    }

    [Fact]
    public async Task Esb_send_uses_bearer_token_and_supplied_4040_contract_without_repeated_login()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> first = await provider.SendBatchAsync(Requests(), CancellationToken.None);
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> second = await provider.SendBatchAsync(Requests(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(ProviderDispatchStatus.Accepted, first.Value[0].Value.Status);
        Assert.Equal(MessageId, first.Value[0].Value.ProviderMessageId);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.Logins);
        Assert.Equal(2, handler.Sends);
        Assert.Equal("Bearer", handler.Scheme);
        Assert.Equal("cached-token", handler.Token);
        using JsonDocument body = JsonDocument.Parse(handler.SendBody!);
        Assert.Equal("40401403", body.RootElement.GetProperty("senders")[0].GetString());
        Assert.Equal("سلام سلام", body.RootElement.GetProperty("messages")[0].GetString());
        Assert.Equal("09135742556", body.RootElement.GetProperty("recipients")[0].GetString());
        Assert.Equal("1234", body.RootElement.GetProperty("uids")[0].GetString());
        Assert.True(provider.SupportsIdempotentResend);
        Assert.Equal(TimeSpan.FromHours(24), provider.UncertainResendWindow);
        Assert.Equal("4040", provider.Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"result\":{\"data\":{\"status\":0,\"smsids\":[]},\"status\":{\"statusCode\":200}},\"status\":{\"statusCode\":200}}")]
    [InlineData("{\"result\":{\"data\":{\"status\":1,\"smsids\":[\"id\"]},\"status\":{\"statusCode\":200}},\"status\":{\"statusCode\":200}}")]
    public async Task Unconfirmed_response_remains_unknown_and_is_never_resent(string body)
    {
        using GatewayHandler handler = new() { SendResponse = body };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(Requests(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.Sends);
    }

    [Fact]
    public async Task Line_without_esb_flag_never_logs_in_or_sends()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string>()));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(Requests(), CancellationToken.None);

        Assert.Equal(ProviderDispatchStatus.RetryableNotSubmitted, result.Value[0].Value.Status);
        Assert.Equal(0, handler.Logins);
        Assert.Equal(0, handler.Sends);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(503, true)]
    public async Task Gateway_errors_preserve_known_vs_unknown_submission(int status, bool unknown)
    {
        using GatewayHandler handler = new() { SendStatus = (HttpStatusCode)status };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(Requests(), CancellationToken.None);

        Assert.Equal(unknown, result.IsFailure);
        if (!unknown)
            Assert.Equal(status == 401 ? ProviderDispatchStatus.RetryableNotSubmitted
                : ProviderDispatchStatus.DefinitelyNotSubmitted, result.Value[0].Value.Status);
        Assert.Equal(1, handler.Sends);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unauthorized_send_or_delivery_evicts_token_for_next_attempt(bool delivery)
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));

        if (delivery)
        {
            handler.DeliveryStatus = HttpStatusCode.Unauthorized;
            Assert.True((await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None)).IsFailure);
            Assert.Equal(1, handler.DeliveryCalls);
            handler.DeliveryStatus = HttpStatusCode.OK;
        }
        else
        {
            handler.SendStatus = HttpStatusCode.Unauthorized;
            Result<IReadOnlyList<Result<ProviderDispatchResult>>> rejected = await provider.SendBatchAsync(Requests(), CancellationToken.None);
            Assert.Equal(ProviderDispatchStatus.RetryableNotSubmitted, rejected.Value[0].Value.Status);
            Assert.Equal(401, rejected.Value[0].Value.ProviderResultCode);
            Assert.Equal(1, handler.Sends);
            handler.SendStatus = HttpStatusCode.OK;
        }

        Assert.Equal(1, handler.Logins);
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> accepted = await provider.SendBatchAsync(Requests(), CancellationToken.None);
        Assert.Equal(ProviderDispatchStatus.Accepted, accepted.Value[0].Value.Status);
        Assert.Equal("cached-token-2", handler.Token);
        Assert.True((await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None)).IsSuccess);
        Assert.Equal(2, handler.Logins);
        using JsonDocument body = JsonDocument.Parse(handler.SendBody!);
        Assert.Equal("1234", body.RootElement.GetProperty("uids")[0].GetString());
    }

    private static EsbOptions Options() => new() { Enabled = true, Username = "client", Password = "secret" };

    [Theory]
    [InlineData(401, FailureImpact.NotSubmitted)]
    [InlineData(403, FailureImpact.NotSubmitted)]
    [InlineData(503, FailureImpact.SubmissionUnknown)]
    public async Task Send_http_failures_have_gateway_origin_and_separate_http_code(int status, FailureImpact impact)
    {
        using GatewayHandler handler = new() { SendStatus = (HttpStatusCode)status };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> result = await provider.SendBatchAsync(Requests(), CancellationToken.None);
        OperationFailure diagnostic = (result.IsFailure ? result.Error!.Diagnostic : result.Value[0].Value.Diagnostic)!;

        Assert.NotNull(diagnostic);
        Assert.Equal(FailureOrigin.EsbGateway, diagnostic.Origin);
        Assert.Equal(FailureStage.Submission, diagnostic.Stage);
        Assert.Equal(impact, diagnostic.Impact);
        Assert.Equal(status, diagnostic.HttpStatus);
        Assert.Null(diagnostic.BusinessCode);
        Assert.Null(diagnostic.UpstreamStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_200_business_error_is_not_http_failure_or_delivery_outcome(bool delivery)
    {
        const string body = """{"status":{"statusCode":200},"result":{"status":{"statusCode":200},"data":{"status":42}}} """;
        using GatewayHandler handler = new() { SendResponse = body, DeliveryResponse = body };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        Error error = delivery
            ? (await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None)).Error!
            : (await provider.SendBatchAsync(Requests(), CancellationToken.None)).Error!;

        Assert.Equal(FailureOrigin.SmsProvider, error.Diagnostic!.Origin);
        Assert.Equal(200, error.Diagnostic.HttpStatus);
        Assert.Equal(42, error.Diagnostic.BusinessCode);
        Assert.Null(error.Diagnostic.UpstreamStatus);
        Assert.Equal(delivery ? FailureImpact.DeliveryQueryUnavailable : FailureImpact.SubmissionUnknown, error.Diagnostic.Impact);
    }

    [Theory]
    [InlineData("transport", FailureOrigin.Network)]
    [InlineData("timeout", FailureOrigin.Network)]
    [InlineData("json", FailureOrigin.EsbGateway)]
    public async Task Network_and_malformed_send_failures_do_not_expose_payloads(string fault, FailureOrigin origin)
    {
        using GatewayHandler handler = new() { Fault = fault, SendResponse = "secret-token-and-phone" };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        Error error = (await provider.SendBatchAsync(Requests(), CancellationToken.None)).Error!;
        Assert.Equal(origin, error.Diagnostic!.Origin);
        Assert.Equal(FailureImpact.SubmissionUnknown, error.Diagnostic.Impact);
        Assert.DoesNotContain("secret-token-and-phone", error.Diagnostic.ToJson() + error.Message);
    }

    [Fact]
    public async Task Disabled_route_is_application_validation_not_gateway_failure()
    {
        using GatewayHandler handler = new();
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));
        ProviderDispatchResult result = (await provider.SendBatchAsync(Requests(), CancellationToken.None)).Value[0].Value;
        Assert.Equal(FailureOrigin.Application, result.Diagnostic!.Origin);
        Assert.Equal(FailureStage.Validation, result.Diagnostic.Stage);
        Assert.Null(result.Diagnostic.HttpStatus);
        Assert.Equal(0, handler.Sends);
        Assert.Equal(0, handler.Logins);
    }

    [Fact]
    public async Task Delivery_login_401_is_token_failure_not_a_failed_sms_submission()
    {
        using GatewayHandler handler = new() { LoginStatus = HttpStatusCode.Unauthorized };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens, new Provider4040RouteResolver(new HashSet<string>()));
        Error error = (await provider.GetDeliveryReportsAsync([MessageId], CancellationToken.None)).Error!;
        Assert.Equal(FailureStage.Token, error.Diagnostic!.Stage);
        Assert.Equal(FailureImpact.DeliveryQueryUnavailable, error.Diagnostic.Impact);
        Assert.Equal(401, error.Diagnostic.HttpStatus);
        Assert.Equal(0, handler.Sends);
        Assert.Equal(0, handler.DeliveryCalls);
    }

    [Fact]
    public async Task Failed_gateway_envelope_has_priority_over_business_code_and_http_200()
    {
        using GatewayHandler handler = new()
        {
            SendResponse = """{"status":{"statusCode":200},"result":{"status":{"statusCode":401},"data":{"status":42}}}""",
        };
        using HttpClient client = new(handler);
        EsbOptions options = Options();
        using EsbAccessTokenProvider tokens = new(new ClientFactory(handler), options, TimeProvider.System);
        Provider4040SmsProvider provider = new(client, options, tokens,
            new Provider4040RouteResolver(new HashSet<string> { "40401403" }));
        OperationFailure diagnostic = (await provider.SendBatchAsync(Requests(), CancellationToken.None)).Error!.Diagnostic!;
        Assert.Equal(FailureOrigin.EsbGateway, diagnostic.Origin);
        Assert.Equal(200, diagnostic.HttpStatus);
        Assert.Equal(401, diagnostic.UpstreamStatus);
        Assert.Equal(42, diagnostic.BusinessCode);
        Assert.Equal(FailureImpact.SubmissionUnknown, diagnostic.Impact);
    }
    private static IReadOnlyList<ProviderSendRequest> Requests() =>
        [new(1234, "40401403", "989135742556", "سلام سلام")];

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class GatewayHandler : HttpMessageHandler
    {
        public int Logins { get; private set; }
        public int Sends { get; private set; }
        public int DeliveryCalls { get; private set; }
        public string? DeliveryBody { get; private set; }
        public List<string> DeliveryBodies { get; } = new();
        public string DeliveryResponse { get; init; } =
            """
            {"result":{"data":{"dlrs":{"40401@1405@7@1510@Hawdy4AHJ@1@0@40401403@2556":{"DateDlr":1791788555,"DateSent":1791788553,"dlr":1,"uid":"2556"}},"status":0},"status":{"statusCode":200}},"status":{"statusCode":200,"message":"OK"}}
            """;
        public string? Scheme { get; private set; }
        public string? Token { get; private set; }
        public string? SendBody { get; private set; }
        public string SendResponse { get; init; } = SuccessfulResponse;
        public HttpStatusCode SendStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode DeliveryStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode LoginStatus { get; init; } = HttpStatusCode.OK;
        public string? Fault { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                Logins++;
                string accessToken = Logins == 1 ? "cached-token" : $"cached-token-{Logins}";
                return new HttpResponseMessage(LoginStatus)
                {
                    Content = new StringContent($"{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}", Encoding.UTF8, "application/json"),
                };
            }
            if (request.RequestUri.AbsolutePath == "/Nww-CheckDeliverySms/1.0")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(Logins == 1 ? "cached-token" : $"cached-token-{Logins}", request.Headers.Authorization.Parameter);
                Assert.Equal("Basic bugfix", Assert.Single(request.Headers.GetValues("basicAuthorization")));
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                DeliveryBody = await request.Content.ReadAsStringAsync(cancellationToken);
                DeliveryBodies.Add(DeliveryBody);
                DeliveryCalls++;
                return new HttpResponseMessage(DeliveryStatus)
                {
                    Content = new StringContent(DeliveryResponse, Encoding.UTF8, "application/json"),
                };
            }
            Assert.Equal("https://esb.abfaisfahan.com:8243/Nww-SendSms/1.0", request.RequestUri.AbsoluteUri);
            Sends++;
            if (Fault == "transport") throw new HttpRequestException("secret-token-and-phone");
            if (Fault == "timeout") throw new TaskCanceledException("secret-token-and-phone");
            Scheme = request.Headers.Authorization!.Scheme;
            Token = request.Headers.Authorization.Parameter;
            SendBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(SendStatus)
            {
                Content = new StringContent(SendResponse, Encoding.UTF8, "application/json"),
            };
        }
    }
}
