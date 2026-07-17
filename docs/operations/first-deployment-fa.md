# راهنمای اولین استقرار SmsHubNext

این راهنما برای داده‌ها و تنظیمات کسب‌وکاری بعد از اولین استقرار است. برای نصب سادهٔ برنامه، IIS، Hosting Bundle و تنظیم اتصال SQL ابتدا از [راهنمای اینستالر ویندوز](windows-installer-fa.md) استفاده کنید. migration در اولین startup خود برنامهٔ وب اجرا می‌شود. مسیر دستی همچنان برای عملیات پیشرفته قابل استفاده است.

## 1. پیش‌نیازهای سرور

- SQL Server 2019 یا جدیدتر، روی همین سیستم یا یک سرور قابل دسترس دیگر
- .NET 10 Hosting Bundle برای IIS
- IIS با ASP.NET Core Module
- دسترسی outbound از سرور برنامه به provider پیامک، مثلا Magfa یا Kavenegar
- دسترسی inbound مورد نیاز برای APIهای سازمانی
- یک اپلیکیشن بیرونی که JWT معتبر با همین `Issuer`، `Audience` و کلید مشترک صادر کند

## 2. تنظیمات برنامه

در محیط production مقدارها را در `appsettings.Production.json`، متغیر محیطی، یا مکان امن مورد تایید عملیات قرار دهید. فایل committed `appsettings.json` فقط default است.

حداقل تنظیمات لازم:

```json
{
  "ConnectionStrings": {
    "SmsHubNext": "Server=.;Database=SmsHubNext;Trusted_Connection=True;Application Name=SmsHubNext;MultipleActiveResultSets=True;TrustServerCertificate=True"
  },
  "DataProtection": {
    "KeyRingPath": "C:\\SmsHubNext\\DataProtection-Keys"
  },
  "BearerTokens": {
    "Key": "REPLACE_WITH_THE_SHARED_JWT_SIGNING_KEY",
    "Issuer": "https://aban360.ir/",
    "Audience": "Any",
    "AccessTokenExpirationMinutes": 560,
    "RefreshTokenExpirationMinutes": 810,
    "AllowMultipleLoginsFromTheSameUser": false,
    "AllowSignoutAllUserActiveClients": true
  },
  "Providers": {
    "Magfa": {
      "Enabled": true,
      "BaseUrl": "https://sms.magfa.com",
      "Timeout": "00:00:30",
      "BatchSize": 100
    },
    "Kavenegar": {
      "Enabled": false,
      "BaseUrl": "https://api.kavenegar.com",
      "Timeout": "00:00:30",
      "BatchSize": 200
    }
  }
}
```

نکات امنیتی:

- مقدار `BearerTokens:Key` را در source control نگذارید.
- مسیر `DataProtection:KeyRingPath` باید برای identity اجرای IIS قابل خواندن/نوشتن باشد.
- کلیدهای Data Protection را از دیتابیس جدا نگه دارید؛ این کلیدها برای بازکردن secretهای provider لازم هستند.
- connection string و JWT key را مثل secret عملیاتی مدیریت کنید.

## 3. اجرای migration

برنامه هنگام startup، migrationهای DbUp را خودکار اجرا می‌کند. برای اولین اجرا:

1. در مسیر دستی می‌توانید دیتابیس خالی `SmsHubNext` را از قبل بسازید؛ در نصب ویندوز، خود برنامه در اولین startup و در صورت داشتن مجوز آن را می‌سازد.
2. دسترسی کاربر برنامه به دیتابیس را تنظیم کنید.
3. برنامه را اجرا کنید.
4. لاگ startup را بررسی کنید؛ اگر migration شکست بخورد برنامه fail-fast می‌شود.

بعد از migration، فقط reference data پایه وجود دارد؛ داده‌های عملیاتی نمونه مثل sender line، customer، API key، provider account، tariff و geo section دیگر seed نمی‌شوند و باید واقعی تنظیم شوند.

## 4. داده‌های لازم بعد از نصب

### 4.1 دریافت JWT مدیریتی

همه Controller APIها بجز ارسال واقعی با JWT محافظت می‌شوند. از اپلیکیشن صادرکننده JWT، یک access token معتبر بگیرید و در درخواست‌های مدیریتی استفاده کنید:

```http
Authorization: Bearer <access-token>
```

### 4.2 ساخت customer

```bash
curl -X POST https://SERVER/customers ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"name\":\"Aban Water\",\"code\":\"aban-water\"}"
```

خروجی شامل `id` مشتری است.

### 4.3 شارژ اولیه customer

```bash
curl -X POST https://SERVER/balances/top-up ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"customerId\":1,\"amount\":1000000,\"reference\":\"initial-production-credit\"}"
```

### 4.4 صدور API key برای ارسال واقعی

```bash
curl -X POST https://SERVER/api-keys ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"customerId\":1,\"name\":\"Production sender\"}"
```

کلید plaintext فقط همین یک بار در response برمی‌گردد. آن را در secret store سیستم مصرف‌کننده نگه دارید.

### 4.5 ساخت provider account

نمونه Magfa:

```bash
curl -X POST https://SERVER/provider-accounts ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"providerCode\":\"magfa\",\"displayName\":\"Magfa Main Account\",\"authType\":\"UsernamePasswordDomain\",\"settings\":{\"username\":\"MAGFA_USERNAME\",\"domain\":\"MAGFA_DOMAIN\"},\"secret\":\"MAGFA_SERVICE_PASSWORD\",\"isActive\":true}"
```

نمونه Kavenegar:

```bash
curl -X POST https://SERVER/provider-accounts ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"providerCode\":\"kavenegar\",\"displayName\":\"Kavenegar Main Account\",\"authType\":\"ApiKey\",\"settings\":{},\"secret\":\"KAVENEGAR_API_KEY\",\"isActive\":true}"
```

در پاسخ‌های read/list هیچ secret یا ciphertext برنمی‌گردد؛ فقط `hasSecret` نمایش داده می‌شود.

### 4.6 ثبت sender line واقعی

برای Magfa، `providerId = 1` است. برای Kavenegar، `providerId = 2` است.

```bash
curl -X POST https://SERVER/reference-data/sender-lines ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"providerId\":1,\"lineNumber\":\"3000_REAL_LINE\",\"isSharedLine\":true,\"providerAccountId\":1,\"isActive\":true}"
```

قانون مهم: sender line فعال برای ارسال باید به provider account فعال و دارای secret وصل باشد.

### 4.7 ثبت تعرفه واقعی

فعلا endpoint مدیریتی create tariff نداریم، پس تعرفه اولیه را با SQL عملیاتی ثبت کنید. قیمت‌ها را حتما با قرارداد واقعی provider جایگزین کنید.

نمونه GSM-7 برای همه message typeها:

```sql
INSERT INTO dbo.Tariff
    (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc, EffectiveToUtc, Currency, IsActive)
VALUES
    (1, NULL, 0, SYSUTCDATETIME(), NULL, 'IRR', 1);

DECLARE @TariffId INT = CONVERT(INT, SCOPE_IDENTITY());

INSERT INTO dbo.TariffRate (TariffId, MinChars, MaxChars, PricePerSegment)
VALUES (@TariffId, 0, NULL, 1000.0000);
```

اگر پیام فارسی/UCS-2 ارسال می‌کنید، تعرفه `Encoding = 1` هم لازم است.

### 4.8 ثبت geo section واقعی

اگر گزارش‌گیری جغرافیایی لازم است، province/city/zone واقعی را از API زیر بسازید:

```bash
curl -X POST https://SERVER/reference-data/geo-sections ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"sectionType\":1,\"name\":\"Tehran\",\"code\":\"THR\"}"
```

برای city/zone مقدار `parentGeoSectionId` را هم ارسال کنید.

## 5. تست کوچک بعد از نصب

### 5.1 بررسی سلامت

```bash
curl https://SERVER/health
```

باید پاسخ JSON با status برابر `healthy` یا، در تنظیم اولیه ناقص Provider، `degraded` بگیرید. مسیر
`/health/live` فقط زنده‌بودن process و `/health/ready` آمادگی کامل dependencyها را گزارش می‌کند.
قرارداد فیلدها و معنی هر check در `health-checks-fa.md` مستند شده است.

### 5.2 تست API مدیریتی با JWT

```bash
curl https://SERVER/reference-data/providers ^
  -H "Authorization: Bearer <access-token>"
```

بدون JWT باید `401` بگیرید. با JWT معتبر باید providerها را ببینید.

### 5.3 تست quote

```bash
curl -X POST https://SERVER/tariffs/quote ^
  -H "Authorization: Bearer <access-token>" ^
  -H "Content-Type: application/json" ^
  -d "{\"providerId\":1,\"messageTypeId\":1,\"text\":\"Hello\"}"
```

اگر تعرفه درست ثبت شده باشد، هزینه برمی‌گردد.

### 5.4 تست ارسال واقعی یک پیام

این endpoint با JWT محافظت نمی‌شود؛ با API key مشتری احراز هویت می‌شود:

```bash
curl -X POST https://SERVER/messages ^
  -H "X-Api-Key: <customer-api-key>" ^
  -H "Content-Type: application/json" ^
  -d "{\"senderLine\":\"3000_REAL_LINE\",\"messageTypeId\":1,\"clientBatchId\":\"smoke-001\",\"messages\":[{\"recipient\":\"989120000001\",\"text\":\"Smoke test\"}]}"
```

انتظار:

- پاسخ `202 Accepted`
- `batchId` و `acceptedCount = 1`
- موجودی customer به اندازه هزینه پیام کم شود
- batch ابتدا `Received/Queued` شود و سپس background dispatcher آن را به provider ارسال کند

## 6. چک‌لیست قبل از استفاده واقعی

- JWT key، issuer و audience با اپلیکیشن صادرکننده token یکی است.
- connection string production درست است.
- Data Protection key ring روی مسیر پایدار و امن قرار دارد.
- provider account واقعی ساخته شده و secret دارد.
- sender line واقعی به provider account فعال وصل شده است.
- tariff واقعی برای encodingهای لازم ثبت شده است.
- customer شارژ کافی دارد.
- API key مشتری در سیستم مصرف‌کننده ذخیره شده است.
- provider `Enabled=true` فقط وقتی تنظیم شود که account/line/tariff آماده باشند.
- لاگ‌ها و health check در مانیتورینگ سرور دیده می‌شوند.
