using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports.Ui;

public sealed class TroubleshootingService(UiOperations operations, TroubleshootingHandler handler)
{
    public Task<UiResult<MessageSearchPage>> Search(SearchMessagesRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => handler.Search(request, cancellationToken), token);

    public async Task<UiResult<TroubleshootingReport>> Inspect(long id, bool live, CancellationToken token)
    {
        UiResult<MessageDiagnostics> diagnostic = await operations.Run(
            (identity, cancellationToken) => handler.Inspect(id, live, cancellationToken), token);
        if (!diagnostic.IsSuccess || diagnostic.Value is null)
            return UiResult<TroubleshootingReport>.Failure(diagnostic.ErrorMessage ?? "بررسی پیام انجام نشد.");

        // Give optional file inspection its own authenticated operation and deadline.
        UiResult<TroubleshootingReport> logs = await operations.Run(
            (identity, cancellationToken) => handler.ReadLogs(diagnostic.Value, cancellationToken), token);
        if (token.IsCancellationRequested) return UiResult<TroubleshootingReport>.Failure("درخواست لغو شد.");
        return logs.IsSuccess ? logs : UiResult<TroubleshootingReport>.Success(
            new(diagnostic.Value, [], logs.ErrorMessage ?? "بررسی فایل‌های خطا کامل نشد.", false, 0));
    }
}
