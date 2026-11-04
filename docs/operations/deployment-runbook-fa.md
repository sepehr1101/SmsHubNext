# راهنمای عملیاتی استقرار SmsHubNext

این فایل چک‌لیست مسیر دستی publish، انتقال artifact و smoke test است. برای مسیر پیشنهادی کاربران غیرفنی از [اینستالر ویندوز](windows-installer-fa.md) استفاده کنید. راه‌اندازی داده‌های اولیه در `first-deployment-fa.md` قرار دارد.

## 1. ساخت artifact

برای پابلیش معمول پروژه، از ریشهٔ مخزن فقط این دستور را اجرا کنید:

```powershell
.\scripts\publish-production.ps1
```

این دستور همیشه خروجی `Release / win-x64 / SelfContained=true` را همراه .NET و ASP.NET Core، فایل اجرایی ویندوز، تنظیمات واقعی Production و `web.config` با محیط Production می‌سازد. فایل تنظیمات پیش‌فرض `artifacts\host-settings\appsettings.Production.json` است و باید از قبل موجود باشد. در نبود آن، اسکریپت پیش از ساخت متوقف می‌شود؛ خروجی سبک یا بدون تنظیمات جایگزین نمی‌شود.

خروجی تاریخ‌دار، بدون بازنویسی نسخه‌های قبلی:

- پوشه: `artifacts\publish\SmsHubNext-Production-yyyyMMdd-HHmmss`
- فایل ZIP: `artifacts\publish\SmsHubNext-Production-yyyyMMdd-HHmmss.zip`
- زمان نام فایل: دبی (`UTC+04:00`).

تست‌های واحد به‌صورت پیش‌فرض اجرا می‌شوند. برای اجرای تست‌های یکپارچه نیز:

```powershell
.\scripts\publish-production.ps1 -IncludeIntegrationTests
```

این حالت به Docker/Testcontainers نیاز دارد و کندتر است. وجود فایل اجرایی، محیط اجرا، تنظیمات بسته و ZIP در پایان کنترل می‌شود. اسکریپت به سرور چیزی منتقل نمی‌کند.

برای انتخاب تنظیمات یک محیط دیگر، فقط مسیر فایل را مشخص کنید:

```powershell
.\scripts\publish-production.ps1 -ProductionSettingsPath 'D:\Private\appsettings.Production.json'
```

بسته حاوی تنظیمات خصوصی همان هاست است. تنظیمات در هر دو فایل `appsettings.json` و `appsettings.Production.json` قرار می‌گیرند تا فایل قدیمی روی سرور تنظیمات جدید را بازنویسی نکند. فایل‌های Development و local وارد بسته نمی‌شوند.

[راهنمای کامل اسکریپت‌ها](../../scripts/README.md) جزئیات استفاده را توضیح می‌دهد. `scripts/publish.ps1` ابزار پایهٔ قابل سفارشی‌سازی است و فقط در صورت درخواست صریح برای بسته‌ای متفاوت استفاده می‌شود؛ دستور روزمرهٔ پابلیش، `publish-production.ps1` است.

ورود پنل در Production از `ManagementUi:LoginUserName` و `ManagementUi:LoginPasswordHash` استفاده می‌کند.
هش باید با قالب ASP.NET Core Identity PasswordHasher سازگار باشد؛ رمز خام را در تنظیمات برنامه ذخیره نکنید.
حساب ثابت توسعه فقط در محیط Development و در نبود تنظیمات ورود قابل استفاده است.
کلیدهای `ManagementUi:AdminApiKey` و `ManagementUi:CustomerApiKey` همچنان باید در دیتابیس معتبر باشند؛
رمز ورود پنل جایگزین اعتبارسنجی این کلیدها در مرز عملیات UI نیست.

## 2. تنظیم production

روی سرور، یک فایل تنظیمات واقعی با نام زیر بسازید:

```text
appsettings.Production.json
```

این فایل نباید commit شود. تنظیمات مهم:

- `ConnectionStrings:SmsHubNext`
- `DataProtection:KeyRingPath`
- `BearerTokens:Key`
- `BearerTokens:Issuer`
- `BearerTokens:Audience`
- `Providers:*`

مسیر `DataProtection:KeyRingPath` باید پایدار، امن، و برای کاربر اجرای IIS قابل خواندن/نوشتن باشد.

## 3. استقرار روی IIS

1. zip تولیدشده را روی سرور منتقل کنید.
2. IIS site یا application مربوط به SmsHubNext را stop کنید.
3. فایل‌ها را در مسیر نصب بازنویسی کنید.
4. `appsettings.Production.json` واقعی را کنار فایل‌های publish شده قرار دهید.
5. مطمئن شوید متغیر محیطی `ASPNETCORE_ENVIRONMENT=Production` برای app pool تنظیم شده است.
6. site را start کنید.
7. لاگ startup را بررسی کنید. DbUp migration هنگام startup اجرا می‌شود و در صورت خطا برنامه fail-fast می‌شود.

## 4. smoke test بعد از deploy

برای تست بدون ارسال واقعی:

```powershell
.\scripts\smoke-test.ps1 `
  -BaseUrl "https://SERVER" `
  -JwtToken "<access-token>" `
  -SkipRealSend
```

برای تست ارسال واقعی یک پیام کنترل‌شده:

```powershell
.\scripts\smoke-test.ps1 `
  -BaseUrl "https://SERVER" `
  -JwtToken "<access-token>" `
  -ApiKey "<customer-api-key>" `
  -SenderLine "3000_REAL_LINE" `
  -Recipient "989120000001"
```

این اسکریپت موارد زیر را بررسی می‌کند:

- `/health` (alias کامل readiness؛ قرارداد جزئیات در `health-checks-fa.md`)
- دسترسی JWT به providerها
- quote تعرفه
- ارسال واقعی با `X-Api-Key`، در صورت حذف نکردن ارسال واقعی
- خواندن batch ایجادشده با JWT

## 5. rollback ساده

برای rollback اضطراری:

1. IIS site را stop کنید.
2. پوشه فعلی نصب را backup بگیرید.
3. artifact قبلی را برگردانید.
4. `appsettings.Production.json` و DataProtection keys را حذف یا جایگزین نکنید.
5. site را start کنید.
6. smoke test بدون ارسال واقعی را اجرا کنید.

اگر migration جدید schema را تغییر داده باشد، rollback دیتابیس باید جداگانه و با تصمیم عملیاتی انجام شود. پیش از هر deploy production از دیتابیس backup بگیرید.
