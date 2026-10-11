using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.DeliveryReports;

public sealed class DeliveryReportsService
{
    private readonly ManagementApiClient _api;
    public DeliveryReportsService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<IReadOnlyList<BatchMessageItem>>> BatchMessages(long batchId, CancellationToken token) => _api.Get<IReadOnlyList<BatchMessageItem>>($"batches/{batchId}/messages", token);
    public Task<ApiClientResult<IReadOnlyList<DeliveryReportItem>>> List(long messageId, CancellationToken token) => _api.Get<IReadOnlyList<DeliveryReportItem>>($"delivery-reports?messageId={messageId}", token);
    public Task<ApiClientResult<IngestDeliveryReportResponse>> Ingest(IngestDeliveryReportRequest request, CancellationToken token) => _api.Post<IngestDeliveryReportRequest, IngestDeliveryReportResponse>("delivery-reports", request, token);
}
