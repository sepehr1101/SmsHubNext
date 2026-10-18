using SmsHubNext.Features.Reports;
using SmsHubNext.Features.Reports.Ui;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Enums;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class MessageDiagnosticTextTests
{
    [Fact]
    public void Copy_contains_correlations_and_separates_live_status_from_stored_history()
    {
        MessageDiagnostics report = Example() with
        {
            Live = new ProviderDeliveryReport("sms-123", null, 4) { ProviderSentAtRaw = "raw-provider-time" },
            LiveObservedAtUtc = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc),
        };
        string text = MessageDiagnosticText.Copy(report);
        Assert.Contains("BatchId: 42", text);
        Assert.Contains("uid ارسالی 4040 (شناسه داخلی پیام): 123", text);
        Assert.Contains("ProviderMessageId / smsId: sms-123", text);
        Assert.Contains("dlr زنده: 4", text);
        Assert.Contains("ارسال‌شده به مخابرات", text);
        Assert.Contains("raw-provider-time", text);
        Assert.Contains("2026-10-18T08:00:00.0000000Z", text);
        Assert.Contains("زمان دقیق ارسال و تحویل گوشی", text);
        Assert.Contains("رویدادهای کل دسته", text);
        Assert.Contains("نیازمند بررسی دستی", text);
        Assert.Contains("ممکن است انقضای مهلت استعلام", text);
    }

    [Fact]
    public void Missing_provider_entry_is_not_presented_as_a_zero_status()
    {
        MessageDiagnostics report = Example() with
        {
            Live = new ProviderDeliveryReport("sms-123", null, 0) { HasProviderStatus = false },
            LiveError = "شناسه در پاسخ نبود",
        };
        string text = MessageDiagnosticText.Copy(report);
        Assert.Contains("شناسه در پاسخ نبود", text);
        Assert.DoesNotContain("dlr زنده:", text);
        Assert.DoesNotContain("رسیده به گوشی", MessageDiagnosticText.Dlr("Magfa", 1));
    }

    private static MessageDiagnostics Example()
    {
        DateTime utc = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);
        MessageDiagnosticRow message = new(123, 42, "1405/07/26", utc, "989120000001", "4040", "۴۰۴۰", "40401403",
            "sms-123", "client-message", "client-batch", SendStatus.Submitted, DeliveryStatus.Pending,
            null, BatchStatus.Held, BatchStatusReason.ManualReviewRequired, null,
            utc, null, utc, 2, null, null, utc, utc.AddMinutes(2), 1);
        return new(message, [new(0, DeliveryReportStatus.Expired, utc)],
            [new(MessageBatchEventType.Held, null, "نیازمند بررسی دستی", utc)], utc);
    }
}
