using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Sending.Ui;

public class DailyResendService(UiOperations operations, DailyResendHandler handler)
{
    public virtual Task<UiResult<DailyResendSummary>> Begin(string day, CancellationToken token) =>
        operations.Run((identity, cancellation) => handler.Begin(day, identity, cancellation), token);
    public virtual Task<UiResult<DailyResendSummary>> Status(Guid id, CancellationToken token) =>
        operations.Run((identity, cancellation) => handler.Status(id, identity, cancellation), token);
    public virtual Task<UiResult<DailyResendSummary>> Confirm(Guid id, CancellationToken token) =>
        operations.Run((identity, cancellation) => handler.Confirm(id, identity, cancellation), token);
}
