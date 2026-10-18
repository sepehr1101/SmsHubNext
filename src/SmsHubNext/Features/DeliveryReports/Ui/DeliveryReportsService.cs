using SmsHubNext.Shared.Ui;
using DeliveryReportItem = SmsHubNext.Features.DeliveryReports.DeliveryReport;
using BatchMessageItem = SmsHubNext.Features.Batches.BatchMessage;
using IngestDeliveryReportRequest = SmsHubNext.Features.DeliveryReports.IngestDeliveryReportRequest;
using IngestDeliveryReportResponse = SmsHubNext.Features.DeliveryReports.IngestDeliveryReportResponse;
using SmsHubNext.Features.Batches;
using SmsHubNext.Features.Sending;

namespace SmsHubNext.Features.DeliveryReports.Ui;

public sealed class DeliveryReportsService(
    UiOperations operations,
    ListBatchMessagesHandler listBatchMessagesHandler,
    BrowseBatchesHandler browseBatchesHandler,
    ListDeliveryReportsHandler listDeliveryReportsHandler,
    IngestDeliveryReportHandler ingestDeliveryReportHandler,
    ResendMessageHandler resendMessageHandler)
{
    public Task<UiResult<ResendMessagePreview>> ResendPreview(long messageId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => resendMessageHandler.Preview(messageId, identity, cancellationToken), token);

    public Task<UiResult<ResendMessageResponse>> Resend(long messageId, Guid requestId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => resendMessageHandler.Handle(messageId,
            new ResendMessageRequest { RequestId = requestId }, identity, cancellationToken), token);

    public Task<UiResult<BatchListPage>> Batches(BrowseBatchesRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => browseBatchesHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<BatchMessagesPage>> MessagePage(long batchId, long? afterId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listBatchMessagesHandler.HandlePage(batchId, afterId, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<BatchMessageItem>>> BatchMessages(long batchId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listBatchMessagesHandler.Handle(batchId, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<DeliveryReportItem>>> List(long messageId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listDeliveryReportsHandler.Handle(messageId, cancellationToken), token);

    public Task<UiResult<IngestDeliveryReportResponse>> Ingest(IngestDeliveryReportRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => ingestDeliveryReportHandler.Handle(request, cancellationToken), token);

}
