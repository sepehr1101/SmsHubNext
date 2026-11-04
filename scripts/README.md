# پابلیش استاندارد پروژه

از ریشهٔ پروژه، فقط این دستور را اجرا کنید:

```powershell
.\scripts\publish-production.ps1
```

این همان بستهٔ قابل تحویل قبلی را با مشخصات ثابت می‌سازد:

- `Release`، ویندوز ۶۴ بیتی (`win-x64`) و همراه محیط اجرای .NET و ASP.NET Core.
- تنظیمات واقعی Production از `artifacts\host-settings\appsettings.Production.json`؛ این فایل محلی و gitignored است.
- فایل اجرایی `SmsHubNext.exe` و پیکربندی IIS با محیط `Production`.
- پوشه و ZIP با نام `artifacts\publish\SmsHubNext-Production-yyyyMMdd-HHmmss`؛ زمان نام فایل به وقت دبی است. بسته‌های قبلی بازنویسی نمی‌شوند.

اسکریپت ابتدا وابستگی‌ها را بازیابی، نسخهٔ Release را می‌سازد و تست‌های واحد را اجرا می‌کند؛ سپس از اسکریپت پایهٔ `publish.ps1` برای ساخت و بسته‌بندی استفاده می‌کند. در پایان، وجود محیط اجرا، فایل اجرایی، تنظیمات و محتوای ZIP را کنترل می‌کند. فایل‌های Development و تنظیمات local وارد خروجی نمی‌شوند. اگر تنظیمات Production موجود یا معتبر نباشد، عملیات متوقف می‌شود و بستهٔ متفاوتی جایگزین نمی‌شود.

برای اجرای تست‌های یکپارچه با داکر پیش از پابلیش:

```powershell
.\scripts\publish-production.ps1 -IncludeIntegrationTests
```

برای انتخاب صریح فایل تنظیمات دیگری:

```powershell
.\scripts\publish-production.ps1 -ProductionSettingsPath 'D:\Private\appsettings.Production.json'
```

مسیر نسبی تنظیمات نسبت به ریشهٔ پروژه تفسیر می‌شود. فایل تنظیمات باید شامل تنظیمات کامل هاست باشد؛ محتویات آن در خروجی فرمان چاپ نمی‌شود. بستهٔ Production شامل همین تنظیمات خصوصی است و برای تحویل به سرور همان محیط ساخته می‌شود.

`publish.ps1` ابزار پایه برای خروجی‌های سفارشی است؛ دستور معمول «یک پابلیش بده» همیشه به معنی اجرای `publish-production.ps1` است. نوع بسته فقط با درخواست صریح تغییر می‌کند. این اسکریپت فقط بسته می‌سازد؛ استقرار روی سرور انجام نمی‌دهد.

راهنمای انتقال و راه‌اندازی: [راهنمای استقرار](../docs/operations/deployment-runbook-fa.md).
