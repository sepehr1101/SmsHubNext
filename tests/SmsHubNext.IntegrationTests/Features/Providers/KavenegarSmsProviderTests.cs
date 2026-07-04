using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmsHubNext.Features.Providers;
using SmsHubNext.Features.Providers.Kavenegar;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Providers;

/// <summary>
/// Exercises the Kavenegar provider seam against WireMock. No database or Docker required.
/// </summary>
public sealed class KavenegarSmsProviderTests : IDisposable
{
    private const string SendArrayPath = "/v1/test-key/sms/sendarray.json";

    private readonly WireMockServer _kavenegar = WireMockServer.Start();

    public void Dispose() => _kavenegar.Stop();

    [Fact]
    public async Task Batch_send_uses_sendarray_and_carries_local_message_ids()
    {
        StubSendArray(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [
                { "messageid": 9001, "localid": "111", "status": 1, "sender": "10004346", "receptor": "09120000001" },
                { "messageid": 9002, "localid": "222", "status": 1, "sender": "10004346", "receptor": "09120000002" }
              ]
            }
            """);

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "10004346", "09120000001", "A"),
                new ProviderSendRequest(222, "10004346", "09120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal("9001", batch.Value[0].Value.ProviderMessageId);
        Assert.Equal("9002", batch.Value[1].Value.ProviderMessageId);

        WireMock.Logging.ILogEntry entry = Assert.Single(_kavenegar.LogEntries);
        Assert.Equal(SendArrayPath, entry.RequestMessage.Path);

        string body = WebUtility.UrlDecode(entry.RequestMessage.Body!);
        Assert.Contains("localmessageids=[111,222]", body);
        Assert.Contains("receptor=[\"09120000001\",\"09120000002\"]", body);
    }

    [Fact]
    public async Task Batch_send_correlates_response_by_localid()
    {
        StubSendArray(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [
                { "messageid": 9002, "localid": "222", "status": 11 },
                { "messageid": 9001, "localid": "111", "status": 1 }
              ]
            }
            """);

        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [
                new ProviderSendRequest(111, "10004346", "09120000001", "A"),
                new ProviderSendRequest(222, "10004346", "09120000002", "B"),
            ],
            CancellationToken.None);

        Assert.True(batch.IsSuccess, batch.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.Accepted, batch.Value[0].Value.Status);
        Assert.Equal("9001", batch.Value[0].Value.ProviderMessageId);
        Assert.Equal(ProviderDispatchStatus.Rejected, batch.Value[1].Value.Status);
        Assert.Equal(11, batch.Value[1].Value.ProviderResultCode);
    }

    [Fact]
    public async Task Request_level_insufficient_credit_holds_the_batch()
    {
        StubSendArray(200, """{ "return": { "status": 418, "message": "credit" }, "entries": [] }""");

        Result<ProviderDispatchResult> result = await Send();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(ProviderDispatchStatus.InsufficientCredit, result.Value.Status);
    }

    [Fact]
    public async Task Status_by_localid_returns_existing_provider_message_id()
    {
        StubStatusLocal(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [ { "messageid": 85463238, "localid": "450", "status": 10 } ]
            }
            """);

        Result<string?> result = await NewProvider().ResolveSubmittedMessageIdAsync(450, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("85463238", result.Value);

        WireMock.Logging.ILogEntry entry = Assert.Single(_kavenegar.LogEntries);
        Assert.Equal("/v1/test-key/sms/statuslocalmessageid.json", entry.RequestMessage.Path);
        Assert.Equal("450", Assert.Single(entry.RequestMessage.Query!["localid"]));
    }

    [Fact]
    public async Task Status_by_localid_returns_null_for_unknown_local_id()
    {
        StubStatusLocal(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [ { "messageid": 0, "localid": "450", "status": 100 } ]
            }
            """);

        Result<string?> result = await NewProvider().ResolveSubmittedMessageIdAsync(450, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Statuses_map_kavenegar_codes_to_normalized_outcomes()
    {
        StubStatus(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [
                { "messageid": 1, "status": 10 },
                { "messageid": 2, "status": 11 },
                { "messageid": 3, "status": 4 },
                { "messageid": 4, "status": 100 }
              ]
            }
            """);

        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(["1", "2", "3", "4"], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Dictionary<string, ProviderDeliveryReport> byId = result.Value.ToDictionary(r => r.ProviderMessageId);

        Assert.Equal(DeliveryReportStatus.Delivered, byId["1"].Status);
        Assert.Equal(DeliveryReportStatus.Undelivered, byId["2"].Status);
        Assert.Null(byId["3"].Status);
        Assert.Equal(DeliveryReportStatus.Expired, byId["4"].Status);
    }

    [Fact]
    public async Task Statuses_are_chunked_by_the_provider_limit()
    {
        StubStatus(200, """{ "return": { "status": 200, "message": "ok" }, "entries": [] }""");

        string[] ids = Enumerable.Range(1, KavenegarOptions.MaxStatusesPerRequest + 1)
            .Select(i => i.ToString())
            .ToArray();

        Result<IReadOnlyList<ProviderDeliveryReport>> result =
            await NewProvider().GetDeliveryReportsAsync(ids, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, _kavenegar.LogEntries.Count());
    }

    [Fact]
    public async Task Fetch_inbound_returns_all_messages_fetched_from_a_destructive_receive()
    {
        StubReceive(200, """
            {
              "return": { "status": 200, "message": "ok" },
              "entries": [
                { "messageid": 1, "message": "A", "sender": "09120000001", "receptor": "10004346", "date": 1800000001 },
                { "messageid": 2, "message": "B", "sender": "09120000002", "receptor": "10004346", "date": 1800000002 }
              ]
            }
            """);

        Result<IReadOnlyList<ProviderInboundMessage>> result =
            await NewProvider().FetchInboundMessagesAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Count);
    }

    private void StubSendArray(int statusCode, string body) =>
        _kavenegar
            .Given(Request.Create().WithPath(SendArrayPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubStatusLocal(int statusCode, string body) =>
        _kavenegar
            .Given(Request.Create().WithPath("/v1/test-key/sms/statuslocalmessageid.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubStatus(int statusCode, string body) =>
        _kavenegar
            .Given(Request.Create().WithPath("/v1/test-key/sms/status.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private void StubReceive(int statusCode, string body) =>
        _kavenegar
            .Given(Request.Create().WithPath("/v1/test-key/sms/receive.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((HttpStatusCode)statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private async Task<Result<ProviderDispatchResult>> Send()
    {
        Result<IReadOnlyList<Result<ProviderDispatchResult>>> batch = await NewProvider().SendBatchAsync(
            [new ProviderSendRequest(1, "10004346", "09120000001", "Hello")],
            CancellationToken.None);

        if (batch.IsFailure)
            return batch.Error!;
        return batch.Value[0];
    }

    private KavenegarSmsProvider NewProvider()
    {
        HttpClient httpClient = new() { BaseAddress = new Uri(_kavenegar.Urls[0]) };
        KavenegarOptions options = new()
        {
            BatchSize = 200,
            Accounts =
            [
                new KavenegarAccount
                {
                    ApiKey = "test-key",
                    SenderLines = ["10004346"],
                    InboundLines = ["10004346"],
                },
            ],
        };

        return new KavenegarSmsProvider(
            httpClient,
            new KavenegarAccountResolver(options),
            options,
            NullLogger<KavenegarSmsProvider>.Instance);
    }
}
