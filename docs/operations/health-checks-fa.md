# قرارداد سلامت و پایش SmsHubNext

این سند قرارداد endpointهای سلامت، معنی وضعیت‌ها و داده‌هایی است که پنل React می‌تواند برای داشبورد سلامت نمایش دهد. Health عمداً سبک نگه داشته شده است: فقط وابستگی‌ها و منابعی بررسی می‌شوند که خود SmsHubNext واقعاً استفاده می‌کند.

## Endpointها

| مسیر | کاربرد | Checkهای اجراشده |
|---|---|---|
| `GET /health/live` | آیا process وب زنده و پاسخ‌گو است؟ | هیچ dependency خارجی اجرا نمی‌شود. |
| `GET /health/ready` | آیا برنامه برای کار عادی آماده است؟ | SQL Server، storage، memory، Secretهای Provider و workerها |
| `GET /health` | alias سازگار با نسخه‌های قبلی و Installer | همان `/health/ready` |

`/health/live` نباید به SQL Server یا Provider وابسته شود. خرابی یک dependency نباید IIS را وارد چرخه restart بی‌فایده کند. Installer و smoke test فعلی می‌توانند همچنان از `/health` استفاده کنند.

## وضعیت‌ها و HTTP status

| وضعیت | معنی | HTTP |
|---|---|---:|
| `healthy` | وضعیت عادی | 200 |
| `degraded` | سرویس قابل استفاده است ولی نیاز به توجه دارد | 200 |
| `unhealthy` | یک dependency ضروری اجازه پردازش امن درخواست‌ها را نمی‌دهد | 503 |

## شکل پاسخ

```json
{
  "service": "SmsHubNext",
  "version": "1.0.0",
  "environment": "Production",
  "status": "healthy",
  "checkedAtUtc": "2026-07-17T12:00:00Z",
  "uptimeSeconds": 3600,
  "totalDurationMs": 18.42,
  "checks": [
    {
      "name": "sql-server",
      "status": "healthy",
      "description": "SQL Server is online, writable, and responsive.",
      "durationMs": 7.31,
      "data": {
        "databaseStatus": "ONLINE",
        "updateability": "READ_WRITE",
        "latencyMs": 6.92,
        "clockSkewSeconds": 0.04
      }
    }
  ]
}
```

پاسخ عمداً شامل stack trace، exception، connection string، مسیر کامل فایل‌ها، API key یا Provider secret نیست.

## Checkهای readiness

### `sql-server`

- بازشدن connection و اجرای یک query کوچک؛
- `ONLINE` بودن دیتابیس؛
- `READ_WRITE` بودن دیتابیس؛
- مدت اتصال/query؛
- اختلاف ساعت UTC برنامه و SQL Server.

قواعد فعلی:

- خطای اتصال، timeout، دیتابیس غیرقابل‌نوشتن یا اختلاف ساعت بیشتر از ۲ دقیقه: `unhealthy`؛
- latency بیشتر از ۱ ثانیه یا اختلاف ساعت بیشتر از ۳۰ ثانیه: `degraded`؛
- timeout کل check پنج ثانیه است.

Migration جداگانه در Health تکرار نمی‌شود؛ برنامه migrationهای DbUp را هنگام startup اجرا می‌کند و در صورت شکست fail-fast می‌شود.

### `storage`

فقط volumeهای واقعاً مورد استفاده برنامه بررسی می‌شوند:

- مسیر اجرای برنامه؛
- مسیر `DataProtection:KeyRingPath`.

کمتر از ۱۰٪ فضای آزاد `degraded` و کمتر از ۵٪ `unhealthy` است. مسیرهای فایل SQL Server بررسی نمی‌شوند، چون SQL Server ممکن است روی سرور دیگری باشد و پایش فایل‌های data/log وظیفه عملیات SQL است.

### `process-memory`

داده‌های زیر برای داشبورد گزارش می‌شوند:

- working set فرایند؛
- managed heap؛
- اندازه و fragmentation حافظه GC؛
- memory load و high-memory threshold تشخیص‌داده‌شده توسط GC؛
- حافظه قابل استفاده برای فرایند.

وقتی فشار حافظه به ۹۰٪ high-memory threshold برسد وضعیت `degraded` می‌شود. memory به‌تنهایی `unhealthy` برنمی‌گرداند تا یک spike کوتاه باعث restart پی‌درپی IIS نشود.

### `provider-secrets`

این check فقط برای Magfa/Kavenegar فعال اجرا می‌شود. به‌جای یک round-trip مصنوعی، `SecretEncrypted` واقعی Provider Accountهای فعال از SQL خوانده و با Key Ring جاری باز می‌شود. به این ترتیب حذف یا تعویض اشتباه Key Ring قابل تشخیص است.

- Provider خارجی غیرفعال: `healthy` و not applicable؛
- Provider فعال بدون account فعال یا با Secret غیرقابل رمزگشایی: `degraded`؛
- فقط تعداد accountهای سالم/ناسالم گزارش می‌شود و Secret هرگز نمایش داده نمی‌شود؛
- هیچ درخواست شبکه یا SMS آزمایشی به Provider فرستاده نمی‌شود.

### `background-workers`

workerهای واقعی پروژه با heartbeat درون‌حافظه‌ای و بدون write اضافه در SQL پایش می‌شوند:

- Dispatch همیشه؛
- Delivery Report polling همیشه؛
- Inbound polling فقط وقتی `InboundPolling:Enabled=true` باشد.

برای هر worker وضعیت اجرا، آخرین cycle موفق، آخرین failure و تعداد failureهای متوالی گزارش می‌شود. Dispatch بعد از ۲ دقیقه و workerهای polling بعد از ۵ دقیقه بدون cycle موفق، `degraded` می‌شوند. مشکل worker readiness را 503 نمی‌کند؛ صف SQL همچنان مرجع بازیابی است و برنامه می‌تواند بعد از رفع مشکل ادامه دهد.

## داده‌های عملیاتی خارج از Health

داشبورد React برای وضعیت صف باید از endpoint موجود زیر استفاده کند:

```text
GET /dispatch/operations/summary
```

این endpoint تعداد batch/message، batchهای due/held/failed، بیشترین تلاش ارسال و قدیمی‌ترین batch باز را برمی‌گرداند. این aggregateها داخل `/health` تکرار نشده‌اند تا probe پرتکرار روی جدول‌های حجیم بار اضافه ایجاد نکند.

موارد زیر نیز متعلق به پایش سرور یا SQL Operations هستند، نه endpoint برنامه:

- آخرین backup موفق و آزمون restore؛
- فضای volumeهای data/log دیتابیس و مصرف transaction log؛
- CPU پایدار، نرخ HTTP 5xx و latencyهای percentile؛
- وضعیت WAS/W3SVC، App Pool، recycleها و انقضای TLS certificate.

Installer برای اجرای مداوم workerها App Pool را روی `AlwaysRunning`، idle timeout را روی صفر و IIS Application Initialization/Preload را فعال می‌کند.

## مواردی که عمداً بررسی نمی‌شوند

- Kafka، RabbitMQ، Redis، Hangfire، SMTP و سرویس‌های دیگری که در پروژه استفاده نشده‌اند؛
- ارسال SMS آزمایشی در هر health probe؛
- ping فعال Magfa/Kavenegar؛ وضعیت شبکه Provider از نتیجه واقعی workerها و داشبورد عملیات مشخص می‌شود؛
- write/delete آزمایشی در دیتابیس یا Key Ring در هر درخواست؛
- scan یا `COUNT` روی جدول میلیاردردیفی `Message`؛
- HealthChecks UI و storage/history جداگانه؛ پنل React خود پروژه مصرف‌کننده JSON خواهد بود؛
- OpenTelemetry/Prometheus، مطابق تصمیم فعلی معماری.

## راهنمای مصرف در React

- برای صفحه داشبورد از `/health/ready` استفاده کنید؛
- بازه polling پیشنهادی ۳۰ تا ۶۰ ثانیه است؛
- `name` و `status` را قرارداد پایدار در نظر بگیرید و `description` را متن تشخیصی بدانید؛
- رنگ‌ها: سبز برای `healthy`، زرد برای `degraded` و قرمز برای `unhealthy`؛
- `checkedAtUtc` و `uptimeSeconds` را در سربرگ، و `durationMs` و `data` هر check را در کارت جزئیات نمایش دهید؛
- برای کارت صف ارسال، داده `/dispatch/operations/summary` را جداگانه دریافت کنید؛
- روی HTTP 503 نیز body JSON را بخوانید؛ پاسخ 503 همچنان قرارداد کامل Health را دارد؛
- داده آخر موفق را با علامت stale نگه دارید تا قطع موقت شبکه پنل باعث خالی‌شدن داشبورد نشود.
