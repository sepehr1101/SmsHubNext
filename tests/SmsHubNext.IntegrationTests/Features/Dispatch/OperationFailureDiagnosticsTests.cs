using System.Net;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Features.Providers.Esb;
using SmsHubNext.Features.Reports;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.IntegrationTests.Features.Dispatch;

public sealed partial class MessageDispatcherTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Esb_auth_failure_records_one_typed_event_for_batch_and_preserves_queued_messages(bool login)
    {
        (long batchId, short customerId) = await PrepareEsbBatch(3);
        using UnauthorizedGateway gateway = new(login);
        using HttpClient client = new(gateway);
        EsbOptions options = TestEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        Assert.True(await EsbDispatcher(TestEsbProvider(client, options, tokens)).DispatchNextBatchAsync(CancellationToken.None));

        await using SqlConnection connection = await _db.OpenConnectionAsync();
        long messageId = await connection.ExecuteScalarAsync<long>(
            "SELECT MIN(Id) FROM dbo.Message WHERE MessageBatchId = @batchId", new { batchId });
        MessageDiagnostics report = (await new MessageDiagnosticsHandler(_db,
            new SmsProviderRegistry([]), TimeProvider.System).Handle(messageId, false, CancellationToken.None)).Value;
        DiagnosticEvent recorded = Assert.Single(report.Events, item => item.Failure is not null);
        Assert.Equal(MessageBatchEventType.ProviderOperationFailed, recorded.EventType);
        Assert.Equal(login ? FailureStage.Token : FailureStage.Submission, recorded.Failure!.Stage);
        Assert.Equal(FailureOrigin.EsbGateway, recorded.Failure.Origin);
        Assert.Equal(401, recorded.Failure.HttpStatus);
        Assert.Equal(FailureImpact.NotSubmitted, recorded.Failure.Impact);
        Assert.Null(recorded.ProviderResultCode);
        Assert.DoesNotContain("secret-response", recorded.FailureJson!);
        Assert.Equal(3, await CountEsbMessages(connection, batchId, SendStatus.Queued));
        Assert.Equal(7000m, await BalanceAsync(customerId));
        Assert.Empty(report.Deliveries);
    }

    [Fact]
    public async Task Esb_polling_and_live_401_record_query_failure_without_changing_delivery_history()
    {
        long batchId = await PrepareEsbDeliveryBatch(3);
        using UnauthorizedGateway gateway = new(false);
        using HttpClient client = new(gateway);
        EsbOptions options = DeliveryEsbOptions();
        using EsbAccessTokenProvider tokens = new(new EsbTestClientFactory(gateway), options, TimeProvider.System);
        ISmsProvider provider = TestEsbProvider(client, options, tokens);
        Assert.True(await EsbDeliveryPoller(TestEsbProvider(client, options, tokens)).PollNextBatchAsync(CancellationToken.None));
        await AssertEsbDeliveryCounts(batchId, delivered: 0, pending: 3);
        await using SqlConnection connection = await _db.OpenConnectionAsync();
        long messageId = await connection.ExecuteScalarAsync<long>(
            "SELECT MIN(Id) FROM dbo.Message WHERE MessageBatchId = @batchId", new { batchId });
        MessageDiagnosticsHandler diagnostics = new(_db, new SmsProviderRegistry([provider]), TimeProvider.System);
        MessageDiagnostics report = (await diagnostics.Handle(messageId, true, CancellationToken.None)).Value;
        DiagnosticEvent recorded = Assert.Single(report.Events, item => item.Failure is not null);
        Assert.Equal(FailureStage.DeliveryQuery, recorded.Failure!.Stage);
        Assert.Equal(401, recorded.Failure.HttpStatus);
        Assert.Equal(FailureImpact.DeliveryQueryUnavailable, recorded.Failure.Impact);
        Assert.Equal(recorded.Failure, report.LiveFailure);
        Assert.NotNull(report.LiveError);
        Assert.Null(report.Live);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MessageBatchEvent WHERE MessageBatchId = @batchId AND FailureJson IS NOT NULL", new { batchId }));
        await AssertEsbDeliveryCounts(batchId, delivered: 0, pending: 3);
    }

    private sealed class UnauthorizedGateway(bool failLogin) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool login = request.RequestUri!.AbsolutePath == "/token";
            return Task.FromResult(new HttpResponseMessage(login && !failLogin ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(login && !failLogin
                    ? """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""
                    : "secret-response", Encoding.UTF8, "application/json"),
            });
        }
    }
}
