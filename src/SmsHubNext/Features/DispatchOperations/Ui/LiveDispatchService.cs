using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.DispatchOperations.Ui;

public sealed class LiveDispatchService(UiOperations operations, LiveDispatchHandler handler)
{
    public Task<UiResult<LiveSnapshot>> Summary(CancellationToken token) => operations.Run((_, ct) => handler.Summary(ct), token);
    public Task<UiResult<LiveMessagePage>> Messages(int hours, byte? status, byte? provider, long? batch, string search, int page, CancellationToken token) =>
        operations.Run((_, ct) => handler.Messages(hours, status, provider, batch, search, page, ct), token);
}
