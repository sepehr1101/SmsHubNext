using MudBlazor;

namespace SmsHubNext.Features.DatabaseMonitoring.Ui;

public sealed record DatabaseMonitorEntry(string Route, string Title, string Description, string Icon, DatabaseMonitorSection Section,
    string Metric1, string Metric2, string Metric3, string Note);

public static class DatabaseMonitoringNavigation
{
    public const string Root = "/admin/settings/database";
    public static readonly DatabaseMonitorEntry[] Pages =
    [
        new("overview", "نمای کلی سلامت", "اتصال، نسخه، حجم داده و مصرف لاگ", Icons.Material.Rounded.Storage, DatabaseMonitorSection.Overview,
            "داده (مگابایت)", "لاگ (مگابایت)", "مصرف لاگ (٪)", "این صفحه فقط اطلاعات پایگاه داده همین برنامه را می‌خواند."),
        new("tables", "حجم و رشد جدول‌ها", "تعداد تقریبی رکوردها و نمودار رشد روزانه", Icons.Material.Rounded.TrendingUp, DatabaseMonitorSection.Tables,
            "رکورد تقریبی", "داده رزروشده (مگابایت)", "ایندکس ثانویه (مگابایت)", "حجم داده شامل ایندکس خوشه‌ای است؛ نمودار از نمونه‌های روزانه UTC ساخته می‌شود و داده‌های گذشته بازسازی نمی‌شوند."),
        new("indexes", "سلامت ایندکس‌ها", "بررسی دستی پراکندگی و تراکم صفحات", Icons.Material.Rounded.ViewList, DatabaseMonitorSection.Indexes,
            "پراکندگی (٪)", "تعداد صفحات", "تراکم صفحات (٪)", "بررسی سبک فقط با درخواست شما اجرا می‌شود. تراکم صفحات در بررسی سبک ناموجود است؛ برای بررسی نمونه‌ای یک ایندکس را انتخاب کنید. درصد پراکندگی به‌تنهایی دلیل ریبیلد نیست. این صفحه مخصوص ایندکس‌های ردیفی است."),
        new("statistics", "آمار بهینه‌ساز", "آخرین به‌روزرسانی و میزان تغییر داده", Icons.Material.Rounded.Analytics, DatabaseMonitorSection.Statistics,
            "رکورد هنگام تهیه آمار", "تغییرات پس از تهیه آمار", "نمونه‌برداری (٪)", "قدیمی بودن زمان آمار به‌تنهایی به معنی نامعتبر بودن آن نیست. داده و الگوی درخواست‌ها نیز باید بررسی شود."),
        new("requests", "درخواست‌های جاری", "زمان اجرا، انتظار و نشست مسدودکننده", Icons.Material.Rounded.Schedule, DatabaseMonitorSection.Requests,
            "زمان اجرا (ثانیه)", "زمان انتظار (ثانیه)", "نشست مسدودکننده", "تصویر لحظه‌ای درخواست‌های همین پایگاه داده؛ هیچ درخواستی قطع نمی‌شود و متن کوئری‌ها نمایش داده نمی‌شود."),
        new("queries", "کوئری‌های پرمصرف", "زمان اجرا، مصرف پردازنده و خواندن داده", Icons.Material.Rounded.Speed, DatabaseMonitorSection.Queries,
            "میانگین اجرا (میلی‌ثانیه)", "پردازنده کل (ثانیه)", "خواندن منطقی کل (مگابایت)", "۵۰ کوئری پرمصرف از بازه‌های Query Store هم‌پوشان با ۲۴ ساعت اخیر؛ فقط اجراهای موفق. میانگین‌ها با تعداد اجرا وزن‌دهی می‌شوند. فعال‌سازی خودکار انجام نمی‌شود."),
        new("operations", "عملیات جاری ایندکس", "وضعیت و پیشرفت عملیات قابل ادامه", Icons.Material.Rounded.Build, DatabaseMonitorSection.Operations,
            "پیشرفت (٪)", "زمان اجرای ثبت‌شده (دقیقه)", "پارتیشن", "فقط عملیات قابل توقف و ادامه قابل مشاهده‌اند؛ خالی بودن این صفحه به معنی نبود هر نوع عملیات نگهداری نیست."),
        new("history", "تاریخچه نگهداری", "آخرین وضعیت مشاهده‌شده عملیات ایندکس", Icons.Material.Rounded.History, DatabaseMonitorSection.History,
            "آخرین پیشرفت (٪)", "زمان اجرای ثبت‌شده (دقیقه)", "فاصله مشاهدات (ثانیه)", "تا ۵۰۰ مشاهده آخر؛ جمع‌آوری هر ۱۵ دقیقه و نگهداری ۹۰ روز. عملیات کوتاه میان دو مشاهده ممکن است ثبت نشوند. زمان نمایش‌داده‌شده آخرین مشاهده است؛ پایان یا موفقیت از ناپدیدشدن عملیات استنتاج نمی‌شود."),
        new("alerts", "هشدارهای قابل اقدام", "مصرف لاگ، انتظار طولانی و توقف عملیات", Icons.Material.Rounded.NotificationsActive, DatabaseMonitorSection.Alerts,
            "مقدار", "", "", "آستانه‌ها: مصرف لاگ از ۸۰٪، انسداد از ۱۰ ثانیه، اجرای درخواست از ۳۰ ثانیه و نمونه روزانه قدیمی‌تر از ۲۶ ساعت. برای لاگ و پیشرفت، مقدار درصد است و برای درخواست‌ها ثانیه. هیچ اقدام اصلاحی خودکار اجرا نمی‌شود."),
    ];
}

