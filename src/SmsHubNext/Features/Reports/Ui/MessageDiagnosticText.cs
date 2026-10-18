using System.Globalization;
using System.Text;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports.Ui;

public static class MessageDiagnosticText
{
    public static string EventStage(MessageBatchEventType type) => type switch
    {
        MessageBatchEventType.AwaitingConfirmation => "مرحله ثبت نزد سرویس؛ پاسخ نامشخص (ارتباط یا پاسخ ناقص)",
        MessageBatchEventType.MessageRejected => "مرحله پذیرش؛ رد اعلام‌شده سرویس‌دهنده",
        MessageBatchEventType.DispatchFailed => "شکست عملیات ارسال؛ علت دقیق در شرح دسته",
        MessageBatchEventType.Held => "توقف عملیات ارسال",
        MessageBatchEventType.Requeued => "بازگشت به صف یا بررسی مجدد",
        MessageBatchEventType.DispatchStarted => "شروع تلاش ارسال دسته",
        MessageBatchEventType.DispatchResumed => "ادامه تلاش ارسال دسته",
        MessageBatchEventType.DeliveryUpdated => "ثبت نتیجه تحویل",
        MessageBatchEventType.DispatchCompleted => "پایان عملیات ارسال",
        MessageBatchEventType.DispatchPartiallyFailed => "پایان ارسال با شکست بخشی از پیام‌ها",
        MessageBatchEventType.DispatchRetryRequested => "درخواست تلاش مجدد",
        _ => "پذیرش درخواست در سامانه",
    };
    public static string Dlr(string provider, int code) => provider == "4040" ? code switch
    {
        -2 => "پیامک حذف‌شده", -1 => "پیام یافت نشد؛ احتمال انتقال به آرشیو",
        0 => "بدون وضعیت؛ احتمال انتظار برای دریافت وضعیت", 1 => "رسیده به گوشی", 2 => "نرسیده به گوشی",
        3 => "ارسال‌نشده به مخابرات", 4 => "ارسال‌شده به مخابرات؛ احتمال تغییر وضعیت هنگام آرشیو",
        5 => "لیست سیاه مخابرات؛ احتمال بازگشت وجه", 8 => "ارسال‌شده به اپراتور پیامکی",
        9 => "بازگشت هزینه به دلیل ترافیک مخابرات", 10 => "صف ارسال", 11 => "اسپم", 12 => "منقضی",
        13 => "بدون مسیر", 14 => "ردشده", 16 => "نرسیده به مخابرات", 17 => "بدون وضعیت دلیوری‌بیس",
        _ => "کد مستندنشده",
    } : "کد خام ارائه‌دهنده؛ تفسیر جدول ۴۰۴۰ برای این ارائه‌دهنده معتبر نیست";

    public static string Send(SendStatus status) => status switch
    {
        SendStatus.Queued => "در صف ارسال", SendStatus.Submitted => "پذیرفته‌شده توسط ارائه‌دهنده",
        SendStatus.Sent => "ارسال‌شده", SendStatus.Rejected => "ردشده", SendStatus.AwaitingConfirmation => "نتیجه ارسال نامشخص؛ انتظار تأیید",
        _ => "نامشخص",
    };
    public static string Delivery(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending => "در انتظار گزارش نهایی", DeliveryStatus.Delivered => "تحویل‌شده",
        DeliveryStatus.Undelivered => "تحویل‌نشده", DeliveryStatus.Expired => "منقضی", _ => "نامشخص",
    };
    public static string Batch(BatchStatus status) => status switch
    {
        BatchStatus.Received => "دریافت‌شده", BatchStatus.Dispatching => "در حال ارسال",
        BatchStatus.DispatchCompleted => "عملیات ارسال تکمیل‌شده", BatchStatus.DispatchPartiallyFailed => "بخشی ناموفق",
        BatchStatus.Held => "نگه‌داشته‌شده", BatchStatus.Rejected => "ردشده", _ => "شکست ارسال",
    };
    public static string Reason(BatchStatusReason? reason) => reason switch
    {
        BatchStatusReason.InsufficientProviderCredit => "کمبود اعتبار ارائه‌دهنده",
        BatchStatusReason.InsufficientCustomerBalance => "کمبود موجودی مشتری",
        BatchStatusReason.DispatchRetriesExhausted => "پایان تلاش‌های خودکار",
        BatchStatusReason.ManualReviewRequired => "نیازمند بررسی دستی",
        BatchStatusReason.InvalidProviderCredentials => "اعتبارنامه نامعتبر", _ => "دلیلی ثبت نشده",
    };
    public static string Time(DateTime? utc) => utc is null ? "ثبت نشده" :
        PersianDisplay.Date(IranTime.FromUtc(new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc))), "yyyy/MM/dd HH:mm:ss") + " (ایران)";

    public static string Assessment(MessageDiagnostics report)
    {
        MessageDiagnosticRow message = report.Message;
        if (message.Status == SendStatus.AwaitingConfirmation)
            return "نتیجه پذیرش پیام مشخص نیست؛ ارسال مجدد ممکن است پیام تکراری ایجاد کند. رویدادهای دسته را بررسی کنید.";
        if (message.Status == SendStatus.Queued)
            return "پیام هنوز در صف سامانه است. دلیل وضعیت دسته و زمان تلاش بعدی را بررسی کنید.";
        if (message.Status == SendStatus.Rejected) return "ارسال پیام رد شده است؛ کد نتیجه و رویدادهای دسته را بررسی کنید.";
        if (report.Live is { HasProviderStatus: true } live && message.ProviderCode == "4040")
            return "پاسخ فعلی ۴۰۴۰: " + Dlr(message.ProviderCode, live.RawStatusCode) + ". این پاسخ ممکن است با نتیجه تاریخی ذخیره‌شده متفاوت باشد.";
        return message.DeliveryStatus == DeliveryStatus.Delivered
            ? "سامانه قبلاً تحویل را ثبت کرده است؛ زمان ثبت، زمان مشاهده گزارش است و لزوماً زمان واقعی رسیدن به گوشی نیست."
            : "علت قطعی از وضعیت داخلی به‌تنهایی مشخص نیست؛ کدهای گزارش تحویل، استعلام زنده و رویدادها را بررسی کنید.";
    }

    public static string Copy(MessageDiagnostics report)
    {
        MessageDiagnosticRow m = report.Message;
        StringBuilder text = new();
        void Add(string label, object? value) => text.Append(label).Append(": ").AppendLine(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "ثبت نشده");
        void Date(string label, DateTime? value)
        {
            Add(label, Time(value));
            if (value.HasValue) Add(label + " UTC", DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        }
        text.AppendLine("گزارش عیب‌یابی پیامک");
        Date("زمان تهیه گزارش", report.ReadAtUtc);
        Add("گیرنده", m.MobileNumber); Add("MessageId", m.Id); Add("BatchId", m.MessageBatchId);
        Add("uid ارسالی 4040 (شناسه داخلی پیام)", m.ProviderCode == "4040" ? m.Id : null);
        Add("ProviderMessageId / smsId", m.ProviderMessageId); Add("ClientBatchId", m.ClientBatchId);
        Add("ClientCorrelatedId", m.ClientCorrelatedId); Add("ارائه‌دهنده", m.ProviderName + " / " + m.ProviderCode);
        Add("خط", m.SenderLine); Add("روز ثبت جلالی در دیتابیس", m.SubmitDateJalali);
        Add("وضعیت ارسال", Send(m.Status)); Add("وضعیت تحویل ذخیره‌شده", Delivery(m.DeliveryStatus));
        Add("وضعیت دسته", Batch(m.BatchStatus)); Add("دلیل دسته", Reason(m.StatusReason));
        Add("کد نتیجه دسته (کد تحویل نیست)", m.ProviderResultCode); Add("تعداد تلاش ارسال دسته", m.DispatchAttemptCount);
        Date("ثبت پیام", m.SubmittedAtUtc); Date("شروع عملیات ارسال دسته", m.DispatchStartedAtUtc);
        Date("پایان عملیات دسته", m.FinishedAtUtc); Date("آخرین تغییر وضعیت دسته", m.StatusChangedAtUtc);
        Date("ثبت پذیرش پیام در صف استعلام (در صورت وجود)", m.DispatchedAtUtc);
        Date("ثبت مشاهده تحویل در سامانه", m.DeliveredAtUtc); Date("شروع انتظار تأیید", m.AwaitingConfirmationSinceUtc);
        Date("تلاش ارسال بعدی", m.NextDispatchAtUtc); Date("استعلام بعدی", m.NextPollAtUtc); Add("تعداد تلاش استعلام صف موجود", m.PollAttempts);
        Add("بررسی اولیه", Assessment(report));
        text.AppendLine("زمان دقیق ارسال و تحویل گوشی از داده‌های ذخیره‌شده قابل اثبات نیست. زمان شروع دسته مربوط به کل دسته است.");
        text.AppendLine("استعلام زنده فقط وضعیت را می‌خواند و وضعیت ذخیره‌شده را تغییر نمی‌دهد.");
        if (report.LiveObservedAtUtc.HasValue) Date("زمان مشاهده پاسخ زنده", report.LiveObservedAtUtc);
        if (report.LiveError is not null) Add("نتیجه استعلام زنده", report.LiveError);
        if (report.Live is { HasProviderStatus: true } live)
        {
            Add("dlr زنده", live.RawStatusCode); Add("تفسیر dlr زنده", Dlr(m.ProviderCode, live.RawStatusCode));
            Add("DateSent خام سرویس (قالب/منطقه زمانی تأیید نشده)", live.ProviderSentAtRaw);
            Add("DateDlr خام سرویس (زمان دریافت گزارش از اپراتور)", live.ProviderDlrAtRaw);
        }
        text.AppendLine("تاریخچه ثبت‌شده تحویل (حداکثر ۱۰۰ مورد آخر؛ وضعیت‌های میانی ذخیره نمی‌شوند):");
        foreach (DiagnosticDelivery d in report.Deliveries)
            Add(Time(d.ReceivedAtUtc), $"raw={d.RawStatusCode}; normalized={d.NormalizedStatus}; {StoredDlr(m.ProviderCode, d)}");
        text.AppendLine("رویدادهای کل دسته (حداکثر ۱۰۰ مورد آخر؛ لزوماً مربوط به همین گیرنده نیستند):");
        foreach (DiagnosticEvent e in report.Events) Add(Time(e.EventTimeUtc), $"{e.EventType}; code={e.ProviderResultCode}; {e.Detail}");
        return text.ToString();
    }

    public static string StoredDlr(string provider, DiagnosticDelivery report) => report.RawStatusCode == 0 && report.NormalizedStatus == DeliveryReportStatus.Expired
        ? "ممکن است انقضای مهلت استعلام در سامانه باشد؛ کد صفر الزاماً پاسخ سرویس نیست"
        : Dlr(provider, report.RawStatusCode);
}
