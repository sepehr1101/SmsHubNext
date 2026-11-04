using System.Text.RegularExpressions;
using SmsHubNext.Features.Reports.Ui;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports;

public sealed record TroubleshootingAssessment(string Result, string Evidence, string Action);

public static partial class TroubleshootingText
{
    public static TroubleshootingAssessment Assess(MessageDiagnostics report)
    {
        MessageDiagnosticRow message = report.Message;
        if (report.Live is { HasProviderStatus: true } live)
        {
            string evidence = $"استعلام زنده در {MessageDiagnosticText.Time(report.LiveObservedAtUtc)}؛ کد {PersianDisplay.Number(live.RawStatusCode)}. وضعیت ذخیره‌شده تغییر نکرده است.";
            if (message.DeliveryStatus is DeliveryStatus.Delivered or DeliveryStatus.Undelivered or DeliveryStatus.Expired)
            {
                TroubleshootingAssessment stored = AssessStored(report);
                if (live.Status is null or DeliveryReportStatus.Unknown)
                    return stored with
                    {
                        Evidence = stored.Evidence + "؛ " + evidence + " پاسخ زنده نتیجه قطعی ندارد.",
                        Action = "پاسخ زنده غیرقطعی، نتیجه ثبت‌شده قبلی را باطل نمی‌کند. " + stored.Action,
                    };
                evidence += " سابقه ذخیره‌شده: " + MessageDiagnosticText.Delivery(message.DeliveryStatus) + ".";
            }
            return live.Status switch
            {
                DeliveryReportStatus.Delivered => new("سرویس‌دهنده اکنون تحویل را گزارش می‌کند.", evidence, "زمان، شماره و متن را با تماس‌گیرنده تطبیق دهید؛ ارسال دوباره برای رفع عدم تحویل پیشنهاد نمی‌شود."),
                DeliveryReportStatus.Undelivered or DeliveryReportStatus.Rejected => new("سرویس‌دهنده اکنون عدم تحویل را گزارش می‌کند.", evidence + " " + MessageDiagnosticText.Dlr(message.ProviderCode, live.RawStatusCode), "علت اعلام‌شده را بررسی کنید؛ مجازبودن ارسال مجدد در مرحله بعد دوباره بررسی می‌شود."),
                DeliveryReportStatus.Expired => new("سرویس‌دهنده اکنون انقضا را گزارش می‌کند.", evidence, "برای پیگیری علت انقضا، گزارش فنی را به پشتیبانی ارائه‌دهنده بدهید."),
                _ => new("استعلام فعلی نتیجه قطعی تحویل ندارد.", evidence, "از ساخت ارسال تکراری خودداری کنید؛ وضعیت ذخیره‌شده و تاریخچه را نیز بررسی کنید."),
            };
        }
        return AssessStored(report);
    }

    private static TroubleshootingAssessment AssessStored(MessageDiagnostics report)
    {
        MessageDiagnosticRow message = report.Message;
        if (message.DeliveryStatus == DeliveryStatus.Delivered)
            return new("تحویل پیام در سامانه ثبت شده است.", "گزارش تحویل ذخیره‌شده؛ زمان مشاهده: " + MessageDiagnosticText.Time(message.DeliveredAtUtc), "شماره، زمان و متن را با تماس‌گیرنده تطبیق دهید؛ در صورت اختلاف، استعلام زنده بگیرید.");
        if (message.Status == SendStatus.AwaitingConfirmation)
            return new("پذیرش پیام توسط سرویس‌دهنده نامشخص است.", "پیام در انتظار تأیید پذیرش است؛ شروع انتظار: " + MessageDiagnosticText.Time(message.AwaitingConfirmationSinceUtc), "ارسال جدید نسازید؛ تلاش‌ها و رویدادهای دسته را بررسی کنید. نبود پاسخ، ردشدن پیام را اثبات نمی‌کند.");
        if (message.Status == SendStatus.Queued)
            return new("پیام هنوز در صف سامانه است.", "وضعیت دسته: " + MessageDiagnosticText.Batch(message.BatchStatus) + "؛ " + MessageDiagnosticText.Reason(message.StatusReason),
                message.StatusReason switch
                {
                    BatchStatusReason.InsufficientProviderCredit => "اعتبار حساب ارائه‌دهنده را بررسی و تأمین کنید؛ سپس وضعیت دسته را پیگیری کنید.",
                    BatchStatusReason.InvalidProviderCredentials => "اطلاعات اتصال حساب ارائه‌دهنده را اصلاح کنید و وضعیت دسته را پیگیری کنید.",
                    _ => "زمان تلاش بعدی و وضعیت دسته را بررسی کنید؛ در صورت توقف طولانی، سلامت پردازشگر ارسال را بررسی کنید.",
                });
        if (message.Status == SendStatus.Rejected)
            return new("ارسال این پیام رد شده است.", "وضعیت ردشدن روی خود پیام ثبت شده است؛ کد نتیجه دسته الزاماً علت همین پیام نیست.", "شرح خطاها و حساب ارائه‌دهنده را بررسی کنید؛ پس از رفع علت، از مسیر مجاز ارسال مجدد اقدام کنید.");
        DiagnosticDelivery? delivery = report.Deliveries.OrderByDescending(item => item.ReceivedAtUtc).FirstOrDefault();
        if (message.DeliveryStatus is DeliveryStatus.Undelivered or DeliveryStatus.Expired)
            return new("نتیجه ذخیره‌شده: " + MessageDiagnosticText.Delivery(message.DeliveryStatus),
                delivery is null ? "گزارش جزئی نگهداری نشده است؛ علت دقیق قابل اثبات نیست." : MessageDiagnosticText.StoredDlr(message.ProviderCode, delivery),
                "استعلام زنده بگیرید؛ انقضای مهلت استعلام سامانه به‌تنهایی عدم تحویل نزد سرویس‌دهنده را ثابت نمی‌کند.");
        return new("نتیجه قطعی تحویل در دسترس نیست.", "وضعیت ارسال: " + MessageDiagnosticText.Send(message.Status) + "؛ تحویل: " + MessageDiagnosticText.Delivery(message.DeliveryStatus),
            "استعلام زنده بگیرید و زمان استعلام بعدی را بررسی کنید. خطای استعلام، دلیل تحویل‌نشدن نیست.");
    }

    public static string Age(DateTime utc, DateTimeOffset now)
    {
        TimeSpan elapsed = now.UtcDateTime - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (elapsed < TimeSpan.Zero) return "زمان ثبت در آینده است؛ ساعت سامانه را بررسی کنید";
        if (elapsed.TotalMinutes < 1) return "کمتر از یک دقیقه پیش";
        if (elapsed.TotalHours < 1) return PersianDisplay.Number((int)elapsed.TotalMinutes) + " دقیقه پیش";
        if (elapsed.TotalDays < 1) return PersianDisplay.Number((int)elapsed.TotalHours) + " ساعت پیش";
        return PersianDisplay.Number((int)elapsed.TotalDays) + " روز پیش";
    }

    public static string CustomerReply(MessageDiagnostics report)
    {
        TroubleshootingAssessment assessment = Assess(report);
        return $"بررسی پیامک شماره {PersianDisplay.Text(report.Message.MobileNumber)}، ثبت‌شده در {MessageDiagnosticText.Time(report.Message.SubmittedAtUtc)}:\n{assessment.Result}\n{assessment.Action}";
    }

    public static string Redact(string? text)
    {
        const string hidden = "[اطلاعات حساس این سطر پنهان شد]";
        string sanitized = SensitiveBlock().Replace(text ?? string.Empty, hidden + "\n");
        sanitized = SensitiveAssignment().Replace(sanitized, "${key}" + hidden);
        return string.Join('\n', sanitized.Split('\n').Select(line => SensitiveLine().IsMatch(line) ? hidden : line));
    }

    [GeneratedRegex("""(?im)^[ \t]*["']?(?:password|passwd|pwd|secret|client[-_ ]?secret|api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|authorization|connectionstring)["']?[ \t]*:[ \t]*[|>][-+]?[ \t]*\r?\n(?<indent>[ \t]+)[^\r\n]*(?:\r?\n\k<indent>[^\r\n]*)*(?:\r?\n|\z)""")]
    private static partial Regex SensitiveBlock();

    [GeneratedRegex("""(?i)(?<key>["']?(?:password|passwd|pwd|secret|client[-_ ]?secret|api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|authorization|connectionstring)["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*(?:"|\z)|'(?:\\.|[^'\\])*(?:'|\z)|[^\r\n]+)""")]
    private static partial Regex SensitiveAssignment();

    [GeneratedRegex(@"password|passwd|pwd\s*=|secret|api[-_ ]?key|authorization|bearer\s|access[-_ ]?token|refresh[-_ ]?token|connectionstring|https?://\S+[?@]", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveLine();
}
