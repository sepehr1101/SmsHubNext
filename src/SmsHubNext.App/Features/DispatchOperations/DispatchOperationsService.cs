using SmsHubNext.App.Shared.Api;
namespace SmsHubNext.App.Features.DispatchOperations;
public sealed class DispatchOperationsService
{
    private readonly ManagementApiClient _api; public DispatchOperationsService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<DispatchOperationsSummary>> Summary(bool onlyProblems, CancellationToken token) => _api.Get<DispatchOperationsSummary>($"dispatch/operations/summary?onlyProblems={onlyProblems.ToString().ToLowerInvariant()}", token);
    public Task<ApiClientResult<DispatchOperationsBatchPage>> Batches(bool onlyProblems, int page, int take, CancellationToken token) => _api.Get<DispatchOperationsBatchPage>($"dispatch/operations/batches?onlyProblems={onlyProblems.ToString().ToLowerInvariant()}&page={page}&take={take}", token);
    public Task<ApiClientResult<BatchDetails>> GetBatch(long id, CancellationToken token) => _api.Get<BatchDetails>($"batches/{id}", token);
    public Task<ApiClientResult<IReadOnlyList<BatchMessageDetails>>> Messages(long id, CancellationToken token) => _api.Get<IReadOnlyList<BatchMessageDetails>>($"batches/{id}/messages", token);
    public Task<ApiClientResult<IReadOnlyList<BatchEventDetails>>> Events(long id, CancellationToken token) => _api.Get<IReadOnlyList<BatchEventDetails>>($"batches/{id}/events", token);
    public Task<ApiClientResult<RetryDispatchResponse>> Retry(long id, CancellationToken token) => _api.Post<object, RetryDispatchResponse>($"batches/{id}/retry-dispatch", new { }, token);
}
