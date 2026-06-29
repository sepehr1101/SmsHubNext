using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Magfa;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Providers;

/// <summary>
/// Exercises <see cref="MagfaSmsProvider"/> against a stubbed Magfa <c>/send</c> endpoint
/// (WireMock), asserting the status-code → <see cref="ProviderDispatchResult"/> mapping and the
/// transport-error lane. No database needed; this is the provider seam in isolation.
/// </summary>
public sealed class MagfaSmsProviderTests : IDisposable
{
    private const string SendPath = "/api/http/sms/v2/send";

    private readonly WireMockServer _magfa = WireMockServer.Start();

    public void Dispose() => _magfa.Stop();

    [Fact]
    public async Task Accepted_message_returns_provider_message_id()
    {
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 0, "id": 111111111, "recipient": "989120000000" } ] }
            """);

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Accepted, result.Value.Status);
        Assert.Equal("111111111", result.Value.ProviderMessageId);
    }

    [Fact]
    public async Task Per_message_reject_is_a_rejection_with_the_code_and_no_id()
    {
        // 27 = recipient on operator blacklist (reference §8).
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 27, "recipient": "989120000000" } ] }
            """);

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Rejected, result.Value.Status);
        Assert.Equal(27, result.Value.ProviderResultCode);
        Assert.Null(result.Value.ProviderMessageId);
    }

    [Fact]
    public async Task Request_level_insufficient_credit_holds_the_batch()
    {
        // 14 = insufficient IRR credit, returned at the request level.
        StubSend(200, """{ "status": 14, "messages": [] }""");

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.InsufficientCredit, result.Value.Status);
    }

    [Fact]
    public async Task Per_message_insufficient_credit_holds_the_batch()
    {
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 14, "recipient": "989120000000" } ] }
            """);

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.InsufficientCredit, result.Value.Status);
    }

    [Fact]
    public async Task Per_message_transient_status_is_a_transport_failure()
    {
        // 23 = no capacity to process the request right now → retry (failed Result).
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 23, "recipient": "989120000000" } ] }
            """);

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Provider, result.Error!.Type);
    }

    [Fact]
    public async Task Configuration_fault_is_rejected_so_the_batch_can_finalize()
    {
        // 2 = invalid sender number: a config fault that will never succeed on retry, so the
        // message is rejected (and refunded) rather than looping forever.
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 2, "recipient": "989120000000" } ] }
            """);

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Rejected, result.Value.Status);
        Assert.Equal(2, result.Value.ProviderResultCode);
    }

    [Fact]
    public async Task Http_5xx_is_a_transport_failure()
    {
        StubSend(500, "upstream boom");

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Provider, result.Error!.Type);
    }

    [Fact]
    public async Task Request_carries_basic_auth_and_our_uid()
    {
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 0, "id": 42, "recipient": "989120000000" } ] }
            """);

        await Send(messageId: 9876, recipient: "989120000000");

        WireMock.Logging.ILogEntry entry = Assert.Single(_magfa.LogEntries);
        string authorization = Assert.Single(entry.RequestMessage.Headers!["Authorization"]);
        Assert.Equal("Basic " + ExpectedBasicToken, authorization);

        string body = entry.RequestMessage.Body!;
        Assert.Contains("\"uids\"", body);
        Assert.Contains("9876", body);
        Assert.Contains("989120000000", body);
    }

    [Fact]
    public async Task Statuses_map_magfa_dlr_codes_to_normalized_outcomes()
    {
        // 1 delivered, 2 not-to-handset, 16 not-to-operator, 8 at-operator (in-flight), -1 gone.
        StubStatuses(200, """
            {
              "status": 0,
              "dlrs": [
                { "mid": 1, "status": 1,  "date": "2026-06-28 10:00:00" },
                { "mid": 2, "status": 2,  "date": "2026-06-28 10:00:00" },
                { "mid": 3, "status": 16, "date": "2026-06-28 10:00:00" },
                { "mid": 4, "status": 8,  "date": "2026-06-28 10:00:00" },
                { "mid": 5, "status": -1, "date": "2026-06-28 10:00:00" }
              ]
            }
            """);

        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(["1", "2", "3", "4", "5"], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Dictionary<string, ProviderDeliveryReport> byId = result.Value.ToDictionary(r => r.ProviderMessageId);

        Assert.Equal(DeliveryReportStatus.Delivered, byId["1"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["2"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["3"].Status);
        Assert.Null(byId["4"].Status);                          // in-flight: keep polling
        Assert.Equal(DeliveryReportStatus.Expired, byId["5"].Status);
        Assert.Equal(16, byId["3"].RawStatusCode);              // native code kept verbatim
    }

    [Fact]
    public async Task Statuses_request_level_error_is_a_transport_failure()
    {
        StubStatuses(200, """{ "status": 19, "dlrs": [] }""");

        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(["1"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Provider, result.Error!.Type);
    }

    [Fact]
    public async Task Statuses_with_no_ids_skips_the_call_and_returns_empty()
    {
        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value);
        Assert.Empty(_magfa.LogEntries); // no HTTP call made
    }

    [Fact]
    public async Task Statuses_request_targets_the_comma_joined_mids_path()
    {
        StubStatuses(200, """{ "status": 0, "dlrs": [] }""");

        await NewProvider().GetDeliveryReportsAsync(["111", "222"], CancellationToken.None);

        WireMock.Logging.ILogEntry entry = Assert.Single(_magfa.LogEntries);
        Assert.EndsWith("/api/http/sms/v2/statuses/111,222", entry.RequestMessage.Path);
    }

    [Fact]
    public async Task Batch_send_issues_one_request_carrying_every_message()
    {
        StubSend(200, """
            { "status": 0, "messages": [
                { "status": 0, "id": 1, "userId": 111, "recipient": "989120000001" },
                { "status": 0, "id": 2, "userId": 112, "recipient": "989120000002" }
            ] }
            """);

        await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "30001234", "989120000001", "A"),
                new ProviderSendRequest(112, "30001234", "989120000002", "B"),
            ],
            CancellationToken.None);

        WireMock.Logging.ILogEntry entry = Assert.Single(_magfa.LogEntries); // exactly one HTTP call for both
        string body = entry.RequestMessage.Body!;
        Assert.Contains("989120000001", body);
        Assert.Contains("989120000002", body);
    }

    [Fact]
    public async Task Batch_send_correlates_each_result_to_its_message_by_uid()
    {
        // Two messages; Magfa returns them out of order — one accepted, one blacklisted (27).
        StubSend(200, """
            { "status": 0, "messages": [
                { "status": 27, "userId": 222, "recipient": "989120000002" },
                { "status": 0, "id": 555, "userId": 111, "recipient": "989120000001" }
            ] }
            """);

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "30001234", "989120000001", "A"),
                new ProviderSendRequest(222, "30001234", "989120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(2, batch.Value.Count);

        // Aligned to input order by uid, not by the response's order.
        Assert.Equal(ProviderDispatchStatus.Accepted, batch.Value[0].Value.Status);
        Assert.Equal("555", batch.Value[0].Value.ProviderMessageId);
        Assert.Equal(ProviderDispatchStatus.Rejected, batch.Value[1].Value.Status);
        Assert.Equal(27, batch.Value[1].Value.ProviderResultCode);
    }

    [Fact]
    public async Task Batch_send_marks_a_message_absent_from_the_response_as_transient()
    {
        // The response omits uid 222 entirely; that message must stay retryable (inner failure).
        StubSend(200, """
            { "status": 0, "messages": [ { "status": 0, "id": 1, "userId": 111, "recipient": "989120000001" } ] }
            """);

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "30001234", "989120000001", "A"),
                new ProviderSendRequest(222, "30001234", "989120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.True(batch.Value[0].IsSuccess);  // 111 accepted
        Assert.True(batch.Value[1].IsFailure);  // 222 missing -> transient, retried next cycle
    }

    [Fact]
    public async Task Mid_lookup_returns_the_provider_id_when_the_message_was_accepted()
    {
        StubMid(200, """{ "status": 0, "mid": 555 }""");

        Result<string?> result = await NewProvider().ResolveSubmittedMessageIdAsync(123, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("555", result.Value);
    }

    [Fact]
    public async Task Mid_lookup_returns_null_when_the_message_is_unknown()
    {
        // status 0 with mid -1 = valid request, no record for this uid (safe to re-send).
        StubMid(200, """{ "status": 0, "mid": -1 }""");

        Result<string?> result = await NewProvider().ResolveSubmittedMessageIdAsync(123, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Mid_lookup_request_error_is_transient()
    {
        StubMid(200, """{ "status": 18, "mid": -1 }""");

        Result<string?> result = await NewProvider().ResolveSubmittedMessageIdAsync(123, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Provider, result.Error!.Type);
    }

    [Fact]
    public async Task Mid_lookup_targets_the_uid_path()
    {
        StubMid(200, """{ "status": 0, "mid": 1 }""");

        await NewProvider().ResolveSubmittedMessageIdAsync(98765, CancellationToken.None);

        WireMock.Logging.ILogEntry entry = Assert.Single(_magfa.LogEntries);
        Assert.EndsWith("/api/http/sms/v2/mid/98765", entry.RequestMessage.Path);
    }

    [Fact]
    public async Task Fetch_inbound_maps_each_message()
    {
        StubMessages(200, """
            { "status": 0, "messages": [
                { "body": "سلام", "senderNumber": "989120000001", "recipientNumber": "983000711", "date": "2026-06-29 09:00:00" },
                { "body": "hi", "senderNumber": "989120000002", "recipientNumber": "983000711", "date": "2026-06-29 09:05:00" }
            ] }
            """);

        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(50, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("989120000001", result.Value[0].SenderNumber);
        Assert.Equal("983000711", result.Value[0].RecipientNumber);
        Assert.Equal("سلام", result.Value[0].Body);
        Assert.Equal("2026-06-29 09:00:00", result.Value[0].ProviderTimestamp);
    }

    [Fact]
    public async Task Fetch_inbound_request_error_is_transient()
    {
        StubMessages(200, """{ "status": 18, "messages": [] }""");

        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(50, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Provider, result.Error!.Type);
    }

    [Fact]
    public async Task Fetch_inbound_targets_the_count_path()
    {
        StubMessages(200, """{ "status": 0, "messages": [] }""");

        await NewProvider().FetchInboundMessagesAsync(25, CancellationToken.None);

        WireMock.Logging.ILogEntry entry = Assert.Single(_magfa.LogEntries);
        Assert.EndsWith("/api/http/sms/v2/messages/25", entry.RequestMessage.Path);
    }

    private void StubMessages(int statusCode, string body) =>
        _magfa
            .Given(Request.Create().WithPath(p => p.StartsWith("/api/http/sms/v2/messages/")).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubMid(int statusCode, string body) =>
        _magfa
            .Given(Request.Create().WithPath(p => p.StartsWith("/api/http/sms/v2/mid/")).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubStatuses(int statusCode, string body) =>
        _magfa
            .Given(Request.Create().WithPath(p => p.StartsWith("/api/http/sms/v2/statuses/")).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubSend(int statusCode, string body) =>
        _magfa
            .Given(Request.Create().WithPath(SendPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    // Collapse a single-message batch back to one result: an outer transport failure or the single
    // inner per-message result — so the per-message assertions read the same as a one-by-one send.
    private async Task<Result<ProviderDispatchResult>> Send(
        long messageId = 1, string recipient = "989120000000")
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [new ProviderSendRequest(messageId, "30001234", recipient, "Hello")],
            CancellationToken.None);

        if (batch.IsFailure)
            return batch.Error!;
        return batch.Value[0];
    }

    private MagfaSmsProvider NewProvider()
    {
        // Credentials are per account and set per request by the provider, so the client carries none.
        HttpClient httpClient = new() { BaseAddress = new Uri(_magfa.Urls[0]) };
        MagfaOptions options = new()
        {
            BatchSize = 100,
            Accounts =
            [
                new MagfaAccount
                {
                    Username = "user",
                    Domain = "domain",
                    Password = "secret",
                    SenderLines = ["30001234"],
                },
            ],
        };
        return new MagfaSmsProvider(
            httpClient, new MagfaAccountResolver(options), options, NullLogger<MagfaSmsProvider>.Instance);
    }

    private static string ExpectedBasicToken =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes("user/domain:secret"));
}
