using SmsHubNext.Shared.Ui;
using DispatchOperationsSummary = SmsHubNext.Features.DispatchOperations.DispatchOperationsSummary;
using DispatchOperationsBatchPage = SmsHubNext.Features.DispatchOperations.DispatchOperationsBatchPage;
using BatchDetails = SmsHubNext.Features.Batches.Batch;
using BatchMessageDetails = SmsHubNext.Features.Batches.BatchMessage;
using BatchEventDetails = SmsHubNext.Features.Batches.BatchEvent;
using RetryDispatchResponse = SmsHubNext.Features.Batches.RetryDispatchResponse;
using SmsHubNext.Features.Batches;

namespace SmsHubNext.Features.DispatchOperations.Ui;

public sealed class DispatchOperationsService(
    UiOperations operations,
    DispatchOperationsHandler dispatchOperationsHandler,
    GetBatchHandler getBatchHandler,
    ListBatchMessagesHandler listBatchMessagesHandler,
    ListBatchEventsHandler listBatchEventsHandler,
    RetryDispatchHandler retryDispatchHandler)
{
    public Task<UiResult<DispatchOperationsSummary>> Summary(bool onlyProblems, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => dispatchOperationsHandler.Summary(new DispatchOperationsRequest { OnlyProblems = onlyProblems }, cancellationToken), token);

    public Task<UiResult<DispatchOperationsBatchPage>> Batches(bool onlyProblems, int page, int take, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => dispatchOperationsHandler.Batches(new DispatchOperationsRequest { OnlyProblems = onlyProblems, Page = page, Take = take }, cancellationToken), token);

    public Task<UiResult<BatchDetails>> GetBatch(long id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => getBatchHandler.Handle(id, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<BatchMessageDetails>>> Messages(long id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listBatchMessagesHandler.Handle(id, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<BatchEventDetails>>> Events(long id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listBatchEventsHandler.Handle(id, cancellationToken), token);

    public Task<UiResult<RetryDispatchResponse>> Retry(long id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => retryDispatchHandler.Handle(id, cancellationToken), token);

}
