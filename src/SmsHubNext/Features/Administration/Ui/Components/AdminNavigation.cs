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
            new("پایش ارسال", "رسیدگی به دسته‌ها و وضعیت ارسال", "/admin/dispatch-operations", Icons.Material.Rounded.MonitorHeart),
            new("پیام‌های دریافتی", "مشاهده پیام‌های ورودی", "/admin/inbound-messages", Icons.Material.Rounded.MarkEmailUnread),
        ]),
        new("گزارش", "بررسی عملکرد ارسال و نتیجه تحویل", "/admin/report-center", Icons.Material.Rounded.Assessment,
        [
            new("حسابداری", "اعتبار و گردش مالی مشتریان", "/admin/billing", Icons.Material.Rounded.AccountBalanceWallet),
            new("گزارش ارسال و هزینه", "تحلیل بر اساس دوره شمسی، مشتری و منطقه", "/admin/reports", Icons.Material.Rounded.Assessment),
            new("تاریخچه تحویل", "مشاهده جزئیات و تاریخچه وضعیت پیام", "/admin/delivery-reports", Icons.Material.Rounded.FactCheck),
        ]),
        new("تنظیمات", "پیکربندی سامانه، اتصال‌ها و داده‌های مرجع", "/admin/settings", Icons.Material.Rounded.Settings,
        [
            new("تنظیمات عمومی", "واحد پول و تقویم سامانه", "/admin/settings/general", Icons.Material.Rounded.Tune),
            new("مشتریان", "مدیریت مشتریان و کلیدهای API", "/admin/customers", Icons.Material.Rounded.People),
            new("ارائه‌دهندگان", "تعریف و مدیریت ارائه‌دهندگان پیامک", "/admin/providers", Icons.Material.Rounded.Hub),
            new("حساب‌های اتصال", "پیکربندی حساب و اطلاعات اتصال", "/admin/provider-accounts", Icons.Material.Rounded.AdminPanelSettings),
            new("تعرفه‌ها", "قیمت‌گذاری و نسخه‌های تعرفه", "/admin/tariffs", Icons.Material.Rounded.PriceChange),
            new("خطوط ارسال", "مدیریت سرشماره‌ها و مسیر ارسال", "/admin/sender-lines", Icons.Material.Rounded.SimCard),
            new("انواع پیام", "مدیریت دسته‌بندی پیام‌های سازمان", "/admin/message-types", Icons.Material.Rounded.Category),
            new("مناطق جغرافیایی", "استان، شهر و منطقه در یک ساختار", "/admin/geo-sections", Icons.Material.Rounded.Map),
        ]),
    ];
}
