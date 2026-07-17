# راهنمای عملیاتی استقرار SmsHubNext

این فایل چک‌لیست مسیر دستی publish، انتقال artifact و smoke test است. برای مسیر پیشنهادی کاربران غیرفنی از [اینستالر ویندوز](windows-installer-fa.md) استفاده کنید. راه‌اندازی داده‌های اولیه در `first-deployment-fa.md` قرار دارد.

## 1. ساخت artifact

از ریشه repo اجرا کنید:

```powershell
.\scripts\publish.ps1
```

خروجی پیش‌فرض:

- پوشه publish: `artifacts\publish\SmsHubNext`
- فایل zip: `artifacts\publish\SmsHubNext.zip`

اگر می‌خواهید تست‌های یکپارچگی هم قبل از publish اجرا شوند:

```powershell
.\scripts\publish.ps1 -IncludeIntegrationTests
```

این حالت به Docker/Testcontainers نیاز دارد و کندتر است.

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
