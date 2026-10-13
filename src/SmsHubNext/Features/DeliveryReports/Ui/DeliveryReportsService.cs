using SmsHubNext.Shared.Ui;
using DeliveryReportItem = SmsHubNext.Features.DeliveryReports.DeliveryReport;
using BatchMessageItem = SmsHubNext.Features.Batches.BatchMessage;
using IngestDeliveryReportRequest = SmsHubNext.Features.DeliveryReports.IngestDeliveryReportRequest;
using IngestDeliveryReportResponse = SmsHubNext.Features.DeliveryReports.IngestDeliveryReportResponse;
using SmsHubNext.Features.Batches;

namespace SmsHubNext.Features.DeliveryReports.Ui;

public sealed class DeliveryReportsService(
    UiOperations operations,
    ListBatchMessagesHandler listBatchMessagesHandler,
    ListDeliveryReportsHandler listDeliveryReportsHandler,
    IngestDeliveryReportHandler ingestDeliveryReportHandler)
{
    public Task<UiResult<IReadOnlyList<BatchMessageItem>>> BatchMessages(long batchId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listBatchMessagesHandler.Handle(batchId, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<DeliveryReportItem>>> List(long messageId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listDeliveryReportsHandler.Handle(messageId, cancellationToken), token);

    public Task<UiResult<IngestDeliveryReportResponse>> Ingest(IngestDeliveryReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => ingestDeliveryReportHandler.Handle(request, cancellationToken), token);

}
