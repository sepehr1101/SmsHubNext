using SmsHubNext.Features.Reports;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Enums;
using Xunit;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmsHubNext.Features.Administration;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class TroubleshootingTests
{
    [Fact]
    public void Historical_batch_errors_do_not_override_a_delivered_message()
    {
        MessageDiagnostics report = Example() with { Message = Example().Message with { DeliveryStatus = DeliveryStatus.Delivered } };
        TroubleshootingAssessment result = TroubleshootingText.Assess(report);
        Assert.Contains("تحویل پیام", result.Result);
        Assert.DoesNotContain("اعتبار", result.Result);
    }

    [Fact]
    public void Query_error_does_not_claim_non_delivery_and_live_result_is_separate()
    {
        MessageDiagnostics report = Example() with { LiveError = "HTTP 500" };
        Assert.Contains("نتیجه قطعی", TroubleshootingText.Assess(report).Result);
        report = report with { Live = new("id", DeliveryReportStatus.Delivered, 1), LiveObservedAtUtc = report.ReadAtUtc };
        Assert.Contains("اکنون تحویل", TroubleshootingText.Assess(report).Result);
        Assert.Contains("تغییر نکرده", TroubleshootingText.Assess(report).Evidence);
        Assert.Equal(DeliveryStatus.Pending, report.Message.DeliveryStatus);
    }

    [Fact]
    public void Uncertain_acceptance_warns_against_duplicate_and_expiry_is_not_invented_provider_evidence()
    {
        MessageDiagnostics report = Example();
        Assert.Contains("ارسال جدید نسازید", TroubleshootingText.Assess(report with { Message = report.Message with { Status = SendStatus.AwaitingConfirmation } }).Action);
        report = report with { Message = report.Message with { DeliveryStatus = DeliveryStatus.Expired }, Deliveries = [new(0, DeliveryReportStatus.Expired, report.ReadAtUtc)] };
        Assert.Contains("مهلت استعلام", TroubleshootingText.Assess(report).Evidence);
        Assert.Contains("به‌تنهایی", TroubleshootingText.Assess(report).Action);
    }

    [Theory]
    [InlineData("Password=private")]
    [InlineData("Authorization: Bearer private")]
    [InlineData("https://host/send?token=private")]
    [InlineData("client_secret: private")]
    public void Technical_details_hide_sensitive_lines_but_keep_stack_frames(string secret)
    {
        string result = TroubleshootingText.Redact("InvalidOperationException: synthetic\n" + secret + "\n at Provider.Send() line 25");
        Assert.DoesNotContain("private", result);
        Assert.Contains("at Provider.Send()", result);
        Assert.Contains("InvalidOperationException", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("98912000000x")]
    public async Task Wizard_rejects_blank_or_invalid_mobile_before_querying(string value)
    {
        TroubleshootingHandler handler = new(null!, null!, null!, TimeProvider.System);
        Assert.True((await handler.Search(new() { Value = value }, default)).IsFailure);
    }

    [Theory]
    [InlineData("\"password\":\n\"private-multiline\"")]
    [InlineData("password = 'private-\nmultiline'")]
    [InlineData("access_token:\n  private-multiline")]
    [InlineData("password: |\n  private-\n  multiline\n")]
    [InlineData("password: |\n  private-\n  multiline")]
    public void Multiline_credentials_are_removed_without_losing_safe_stack_frames(string credential)
    {
        string text = "InvalidOperationException: synthetic\n" + credential + "\n at TestProvider.Send()";
        string result = TroubleshootingText.Redact(text);
        Assert.DoesNotContain("private-", result);
        Assert.DoesNotContain("multiline", result);
        Assert.Contains("at TestProvider.Send()", result);
    }

    [Theory]
    [InlineData(DeliveryStatus.Delivered, null)]
    [InlineData(DeliveryStatus.Delivered, DeliveryReportStatus.Unknown)]
    [InlineData(DeliveryStatus.Undelivered, null)]
    [InlineData(DeliveryStatus.Expired, null)]
    public void Nonterminal_live_response_preserves_stored_terminal_result(DeliveryStatus stored, DeliveryReportStatus? live)
    {
        MessageDiagnostics report = Example() with { Message = Example().Message with { DeliveryStatus = stored } };
        string storedResult = TroubleshootingText.Assess(report).Result;
        report = report with { Live = new("id", live, -1), LiveObservedAtUtc = report.ReadAtUtc };
        TroubleshootingAssessment result = TroubleshootingText.Assess(report);
        Assert.Equal(storedResult, result.Result);
        Assert.Contains("استعلام زنده", result.Evidence);
        Assert.Contains(storedResult, TroubleshootingText.CustomerReply(report));
    }

    [Theory]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷")]
    [InlineData("+۹۸۹۱۲۱۲۳۴۵۶۷")]
    [InlineData("٩٨٩١٢١٢٣٤٥٦٧")]
    public void Mobile_normalization_preserves_exact_recipient_search(string mobile)
    {
        Assert.Equal("989121234567", new SearchMessagesRequest { Field = MessageSearchField.MobileNumber, Value = mobile }.Normalize().Value);
    }

    private static MessageDiagnostics Example()
    {
        DateTime utc = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        MessageDiagnosticRow message = new(123, 42, "1405/08/12", utc, "989120000001", "4040", "۴۰۴۰", "40401403",
            "id", null, null, SendStatus.Submitted, DeliveryStatus.Pending, null, BatchStatus.Held,
            BatchStatusReason.InsufficientProviderCredit, null, utc, null, utc, 2, null, null, utc, utc.AddMinutes(2), 1);
        return new(message, [], [new(MessageBatchEventType.Held, null, "خطای قدیمی دسته", utc)], utc);
    }

    [Fact]
    public async Task Log_budget_expiry_preserves_diagnostics_but_user_cancellation_propagates()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SmsHubNext-log-budget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "errors-test.jsonl"), "{}\n");
            ListApplicationErrorsHandler reader = new(Options.Create(new ApplicationLogOptions { DirectoryPath = directory }), new LogEnvironment());
            TroubleshootingHandler handler = new(null!, null!, reader, new ExpiredBudgetClock());
            MessageDiagnostics diagnostic = Example();
            Result<TroubleshootingReport> result = await handler.ReadLogs(diagnostic, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Same(diagnostic, result.Value.Diagnostics);
            Assert.Contains("مهلت", result.Value.LogError);
            Assert.Empty(result.Value.Errors);

            using CancellationTokenSource userCancellation = new();
            userCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ReadLogs(diagnostic, userCancellation.Token));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ExpiredBudgetClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(2), dueTime);
            callback(state);
            return new CompletedTimer();
        }
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LogEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SmsHubNext";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
