using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.DispatchOperations.Ui;

public sealed class LiveDispatchService(UiOperations operations, LiveDispatchHandler handler)
{
    public Task<UiResult<LiveSnapshot>> Summary(CancellationToken token) => operations.Run((_, ct) => handler.Summary(ct), token);
    public Task<UiResult<LiveMessagePage>> Messages(LiveMessageRequest request, CancellationToken token) =>
        operations.Run((_, ct) => handler.Messages(request, ct), token);
}
