using SmsHubNext.Features.Administration;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

public sealed class TroubleshootingHandler(SearchMessagesHandler search, MessageDiagnosticsHandler diagnostics,
    ListApplicationErrorsHandler logs, TimeProvider clock)
{
    public Task<Result<MessageSearchPage>> Search(SearchMessagesRequest request, CancellationToken token)
    {
        SearchMessagesRequest query = request with { Field = MessageSearchField.MobileNumber, Limit = 10, IranLocalTime = true };
        query = query.Normalize();
        if (query.Value.Length != 12 || !query.Value.StartsWith("989", StringComparison.Ordinal) || !query.Value.All(char.IsAsciiDigit))
            return Task.FromResult(Result.Failure<MessageSearchPage>(Error.Validation("troubleshoot.mobile", "شماره همراه معتبر مانند ۰۹۱۲۱۲۳۴۵۶۷ وارد کنید.")));
        return search.Handle(query, token);
    }

    public Task<Result<MessageDiagnostics>> Inspect(long messageId, bool live, CancellationToken token) =>
        diagnostics.Handle(messageId, live, token);

    public async Task<Result<TroubleshootingReport>> ReadLogs(MessageDiagnostics report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime from = DateTime.SpecifyKind(report.Message.SubmittedAtUtc, DateTimeKind.Utc).AddMinutes(-1);
        // Operational files have bounded retention. Older messages still retain their business delivery history.
        if (from < now.AddDays(-30)) from = now.AddDays(-30);
        using CancellationTokenSource budget = new(TimeSpan.FromSeconds(2), clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
        Result<ApplicationErrorResults> errors;
        try { errors = await logs.HandleForMessage(from, now, report.Message.Id, linked.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested)
        {
            return new TroubleshootingReport(report, [], "مهلت خواندن فایل‌های خطا پایان یافت؛ نتیجه اصلی پیام آماده است، اما بررسی لاگ کامل نشد.", false, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new TroubleshootingReport(report, [], "دسترسی به فایل‌های خطا ممکن نیست؛ نتیجه بررسی پیام همچنان در دسترس است.", false, 0);
        }
        return new TroubleshootingReport(report, errors.IsSuccess ? errors.Value.Items.Select(item => item with
        {
            Message = TroubleshootingText.Redact(item.Message), Exception = TroubleshootingText.Redact(item.Exception),
        }).ToArray() : [], errors.IsFailure ? errors.Error!.Message : null,
            errors.IsSuccess && errors.Value.Truncated, errors.IsSuccess ? errors.Value.SkippedLines : 0);
    }
}

public sealed record TroubleshootingReport(MessageDiagnostics Diagnostics, IReadOnlyList<ApplicationError> Errors,
    string? LogError, bool LogsTruncated, int SkippedLogLines);
