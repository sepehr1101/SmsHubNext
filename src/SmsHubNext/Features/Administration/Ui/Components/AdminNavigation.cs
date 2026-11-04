using MudBlazor;

namespace SmsHubNext.Features.Administration.Ui.Components;

public sealed record AdminMenuCard(string Title, string Description, string Route, string Icon);
public sealed record AdminMenuGroup(string Title, string Description, string Route, string Icon, AdminMenuCard[] Cards);

public static class AdminNavigation
{
    public static readonly AdminMenuGroup[] Groups =
    [
        new("عملیات پیامک", "ارسال و پیگیری پیام‌های سازمان", "/admin/operations", Icons.Material.Rounded.Send,
        [
            new("ارسال پیامک", "ثبت پیام و انتخاب گیرندگان", "/admin/send", Icons.Material.Rounded.AddComment),
            new("ارسال از اکسل", "ساخت پیام شخصی با ستون‌های فایل اکسل", "/admin/send-excel", Icons.Material.Rounded.TableChart),
            new("ارسال‌ها و نتیجه تحویل", "پیگیری ارسال، نتیجه تحویل و رسیدگی به مشکلات", "/admin/dispatch-operations", Icons.Material.Rounded.MonitorHeart),
            new("پیام‌های دریافتی", "مشاهده پیام‌های ورودی", "/admin/inbound-messages", Icons.Material.Rounded.MarkEmailUnread),
        ]),
        new("گزارش", "بررسی عملکرد ارسال و نتیجه تحویل", "/admin/report-center", Icons.Material.Rounded.Assessment,
        [
            new("حسابداری", "اعتبار و گردش مالی مشتریان", "/admin/billing", Icons.Material.Rounded.AccountBalanceWallet),
            new("گزارش ارسال و هزینه", "تحلیل بر اساس دوره شمسی، مشتری و منطقه", "/admin/reports", Icons.Material.Rounded.Assessment),
            new("خلاصه عملکرد مناطق", "تعداد، هزینه و تحویل در استان، شهر و ناحیه", "/admin/reports/dimensions/geography", Icons.Material.Rounded.Map),
            new("خلاصه انواع پیامک", "حجم و هزینه و تحویل هر نوع پیام", "/admin/reports/dimensions/message-types", Icons.Material.Rounded.Category),
            new("ماتریس منطقه و نوع پیامک", "مقایسه تعداد و هزینه انواع پیام در مناطق", "/admin/reports/dimensions/matrix", Icons.Material.Rounded.TableChart),
            new("روند ماهانه مناطق", "روند حجم، هزینه و نرخ تحویل هر منطقه", "/admin/reports/dimensions/geography-monthly", Icons.Material.Rounded.Insights),
            new("روند ماهانه انواع پیامک", "روند حجم، هزینه و نرخ تحویل هر نوع پیام", "/admin/reports/dimensions/message-types-monthly", Icons.Material.Rounded.DateRange),
            new("جست‌وجوی پیامک", "پیداکردن پیام با شناسه قبض، پرداخت یا شماره تلفن", "/admin/reports/search", Icons.Material.Rounded.Search),
        ]),
        new("تنظیمات", "پیکربندی سامانه، اتصال‌ها و داده‌های مرجع", "/admin/settings", Icons.Material.Rounded.Settings,
        [
            new("سؤال‌های پرتکرار", "مدیریت سؤال و پاسخ و ترتیب نمایش صفحه اصلی", "/admin/settings/faq", Icons.Material.Rounded.HelpOutline),

            new("تنظیمات عمومی", "واحد پول و تقویم سامانه", "/admin/settings/general", Icons.Material.Rounded.Tune),
            new("خطاهای نرم‌افزار", "جست‌وجوی خطاها با تاریخ شمسی و ساعت", "/admin/settings/errors", Icons.Material.Rounded.ErrorOutline),
            new("مشتریان", "مدیریت مشتریان و کلیدهای API", "/admin/customers", Icons.Material.Rounded.People),
            new("ارائه‌دهندگان", "تعریف و مدیریت ارائه‌دهندگان پیامک", "/admin/providers", Icons.Material.Rounded.Hub),
            new("حساب‌های سرویس پیامک", "پیکربندی حساب و اطلاعات اتصال", "/admin/provider-accounts", Icons.Material.Rounded.AdminPanelSettings),
            new("تعرفه‌ها", "قیمت‌گذاری و نسخه‌های تعرفه", "/admin/tariffs", Icons.Material.Rounded.PriceChange),
            new("خطوط ارسال", "مدیریت سرشماره‌ها و مسیر ارسال", "/admin/sender-lines", Icons.Material.Rounded.SimCard),
            new("انواع پیام", "مدیریت دسته‌بندی پیام‌های سازمان", "/admin/message-types", Icons.Material.Rounded.Category),
            new("مناطق جغرافیایی", "استان، شهر و منطقه در یک ساختار", "/admin/geo-sections", Icons.Material.Rounded.Map),
        ]),
        new("پایش پایگاه داده", "سلامت، رشد، ایندکس‌ها و نگهداری SQL Server", "/admin/settings/database", Icons.Material.Rounded.Storage,
            SmsHubNext.Features.DatabaseMonitoring.Ui.DatabaseMonitoringNavigation.Pages.Select(entry =>
                new AdminMenuCard(entry.Title, entry.Description, "/admin/settings/database/" + entry.Route, entry.Icon)).ToArray()),
    ];
}
