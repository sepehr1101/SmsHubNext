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
        Assert.False(provider.SupportsIdempotentResend);
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
            Assert.Equal(ProviderDispatchStatus.DefinitelyNotSubmitted, result.Value[0].Value.Status);
        Assert.Equal(1, handler.Sends);
    }

    private static EsbOptions Options() => new() { Enabled = true, Username = "client", Password = "secret" };
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
        public string DeliveryResponse { get; init; } =
            """
            {"result":{"data":{"dlrs":{"40401@1405@7@1510@Hawdy4AHJ@1@0@40401403@2556":{"DateDlr":1791788555,"DateSent":1791788553,"dlr":1,"uid":"2556"}},"status":0},"status":{"statusCode":200}},"status":{"statusCode":200,"message":"OK"}}
            """;
        public string? Scheme { get; private set; }
        public string? Token { get; private set; }
        public string? SendBody { get; private set; }
        public string SendResponse { get; init; } = SuccessfulResponse;
        public HttpStatusCode SendStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                Logins++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"cached-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}", Encoding.UTF8, "application/json"),
                };
            }
            if (request.RequestUri.AbsolutePath == "/Nww-CheckDeliverySms/1.0")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal("cached-token", request.Headers.Authorization.Parameter);
                Assert.Equal("Basic bugfix", Assert.Single(request.Headers.GetValues("basicAuthorization")));
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                DeliveryBody = await request.Content.ReadAsStringAsync(cancellationToken);
                DeliveryCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DeliveryResponse, Encoding.UTF8, "application/json"),
                };
            }
            Assert.Equal("https://esb.abfaisfahan.com:8243/Nww-SendSms/1.0", request.RequestUri.AbsoluteUri);
            Sends++;
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
