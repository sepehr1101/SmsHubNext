# راهنمای فارسی توسعه فرانت‌اند SmsHubNext

این سند برای برنامه‌نویس فرانت‌اند پروژه نوشته شده است تا بدون نیاز به مطالعه تمام کدهای بک‌اند بداند محصول چیست، احراز هویت چگونه کار می‌کند، مستندات API کجاست و پیاده‌سازی پنل را با چه ترتیبی شروع کند.

> این راهنما بر اساس وضعیت فعلی سورس تهیه شده است. قرارداد نهایی هر API را همیشه در Scalar یا فایل OpenAPI بررسی کنید؛ چون با توسعه پروژه ممکن است جزئیات ورودی و خروجی تغییر کند.

## 1. SmsHubNext چیست؟

SmsHubNext یک سامانه ارسال، حسابداری و گزارش‌گیری پیامک برای سازمان‌ها و شرکت‌ها است. نمونه کاربرد اصلی آن ارسال پیامک قبض، اطلاع‌رسانی سازمانی، پیام تراکنشی و OTP است.

سامانه قرار است این کارها را انجام دهد:

- تعریف مشتری یا سازمان استفاده‌کننده از سرویس؛
- صدور API Key برای هر مشتری؛
- تعریف ارائه‌دهنده پیامک، مانند مگفا و کاوه‌نگار؛
- تعریف شماره‌های فرستنده؛
- تعریف تعرفه و محاسبه هزینه بر اساس متن و تعداد بخش‌های پیامک؛
- نگهداری اعتبار ریالی مشتری و کسر اتمی هزینه ارسال؛
- دریافت یک یا چند پیام و قرار دادن آن‌ها در صف ارسال؛
- ارسال غیرهم‌زمان پیام‌ها توسط سرویس پس‌زمینه؛
- پیگیری وضعیت ارسال و وضعیت تحویل هر پیام؛
- گزارش‌گیری بر اساس مشتری، ارائه‌دهنده، نوع پیام، محدوده جغرافیایی و تاریخ شمسی؛
- نمایش وضعیت عملیاتی صف، خطاها و تلاش‌های مجدد.

هر درخواست ارسال یک «بچ» (`Batch`) می‌سازد. بک‌اند ابتدا پیام‌ها را اعتبارسنجی و قیمت‌گذاری می‌کند، هزینه را از اعتبار مشتری کم می‌کند و پاسخ `202 Accepted` می‌دهد. ارسال واقعی در پس‌زمینه انجام می‌شود؛ بنابراین دریافت پاسخ موفق از `POST /messages` به معنی تحویل پیامک به گوشی نیست، بلکه یعنی درخواست با موفقیت پذیرفته و ذخیره شده است.

## 2. مفاهیم اصلی برای طراحی رابط کاربری

| مفهوم | توضیح |
|---|---|
| Customer | مشتری یا سازمان مالک پیامک‌ها و اعتبار |
| API Key | کلید محرمانه مشتری برای فراخوانی API ارسال |
| Provider | ارائه‌دهنده پیامک، مانند Magfa یا Kavenegar |
| Provider Account | اطلاعات حساب و دسترسی به پنل ارائه‌دهنده |
| Sender Line | شماره‌ای که پیامک از آن ارسال می‌شود |
| Message Type | نوع یا کاربرد پیام، مانند OTP، تراکنشی، انبوه و قبض آب |
| Tariff | تعرفه قیمت‌گذاری بر اساس ارائه‌دهنده، نوع پیام و encoding |
| Balance | اعتبار پیش‌پرداخت مشتری بر حسب ریال |
| Batch | یک درخواست ارسال شامل یک یا چند پیام |
| Message | یک پیام برای یک شماره موبایل مشخص |
| Delivery Report | سابقه تغییر وضعیت تحویل پیام |
| Geo Section | ساختار جغرافیایی سلسله‌مراتبی استان، شهر و ناحیه |

در سیستم، متن هر پیام مستقل است. حتی داخل یک بچ می‌توان برای هر گیرنده متن متفاوتی فرستاد.

## 3. آدرس سرویس و مستندات

پس از نصب محلی با پورت پیش‌فرض، آدرس پایه بک‌اند این است:

```text
http://localhost:8080
```

آدرس‌های مهم:

| کاربرد | آدرس |
|---|---|
| صفحه معرفی سرویس | `http://localhost:8080/` |
| زنده بودن process بک‌اند | `http://localhost:8080/health/live` |
| آمادگی بک‌اند و وابستگی‌ها | `http://localhost:8080/health/ready` |
| مستندات تعاملی Scalar | `http://localhost:8080/scalar/v1` |
| قرارداد خام OpenAPI | `http://localhost:8080/openapi/v1.json` |
| اطلاعات ماشینی سرویس | `http://localhost:8080/service-info` |

در محیطی غیر از سیستم محلی، فقط بخش دامنه و پورت عوض می‌شود؛ برای مثال:

```text
https://sms.example.com/scalar/v1
```

نمایش مستندات با تنظیم `OpenApi:Enabled` کنترل می‌شود. مقدار فعلی آن `true` است. اگر در یک سرور مستندات باز نشد، از مسئول بک‌اند بخواهید این گزینه را برای محیط توسعه فعال کند یا فایل `/openapi/v1.json` را در اختیارتان بگذارد.

## 4. شکل استاندارد پاسخ API

بیشتر endpointها پاسخ را داخل یک envelope مشترک برمی‌گردانند:

```ts
export interface ApiResponse<T> {
  success: boolean;
  code: string;
  message: string;
  data: T | null;
  meta: {
    traceId: string;
    timestampUtc: string;
  };
  errors?: Array<{
    field: string | null;
    code: string;
    message: string;
  }>;
}
```

نمونه پاسخ موفق:

```json
{
  "success": true,
  "code": "ok",
  "message": "OK.",
  "data": {
    "customerId": 1,
    "balance": 950000,
    "updatedAtUtc": "2026-07-15T08:30:00Z"
  },
  "meta": {
    "traceId": "...",
    "timestampUtc": "2026-07-15T08:30:01Z"
  }
}
```

فرانت‌اند نباید موفق بودن درخواست را فقط از HTTP status تشخیص دهد. ابتدا status را بررسی کند و سپس مقدار `success`، `code` و در خطاهای اعتبارسنجی آرایه `errors` را نمایش دهد. مقدار `meta.traceId` را در جزئیات خطا یا بخش پشتیبانی نگه دارید؛ این شناسه برای پیدا کردن درخواست در لاگ بک‌اند مفید است.

## 5. احراز هویت

در پروژه دو نوع credential با دو کاربرد متفاوت وجود دارد.

### 5.1 Bearer Token برای پنل مدیریتی

endpointهای مدیریتی به‌صورت پیش‌فرض انتظار JWT Bearer Token دارند:

```http
Authorization: Bearer <access-token>
```

این توکن برای صفحه‌هایی مانند مشتریان، تعرفه‌ها، اعتبار، گزارش‌ها و تنظیمات استفاده می‌شود.

**وضعیت فعلی مهم:** اعتبارسنجی JWT در بک‌اند فعال است، اما endpoint ورود، صدور access token و refresh token هنوز پیاده نشده است. بنابراین فرانت‌اند فعلاً نباید آدرس، payload یا پاسخ Login را حدس بزند. تا زمان تکمیل Login، برنامه‌نویس فرانت می‌تواند ساخت صفحات، routing، کامپوننت‌ها، لایه API و حالت‌های loading/error را با mock data یا توکن آزمایشی‌ای که بک‌اند در اختیارش می‌گذارد جلو ببرد.

### 5.2 API Key برای هویت مشتری و ارسال پیامک

ارسال پیامک با API Key مشتری انجام می‌شود:

```http
X-Api-Key: shn_...
```

نکات امنیتی:

- API Key فقط هنگام صدور، یک بار به‌صورت کامل برگردانده می‌شود؛ بک‌اند بعداً متن کامل آن را نگهداری یا نمایش نمی‌دهد.
- کلید را داخل سورس React، فایل `.env` قابل انتشار یا مخزن Git قرار ندهید.
- برای تست مرورگر، کلید تست را فقط در حافظه برنامه نگه دارید و با refresh صفحه پاک کنید؛ از `localStorage` برای کلید دائمی مشتری استفاده نکنید.
- کلید عملیاتی مشتری را با کلید مخصوص توسعه عوض نکنید؛ برای توسعه یک مشتری و API Key جدا ساخته شود.
- در صورت تعریف محدودیت IP برای کلید، درخواست فقط از IPهای مجاز پذیرفته می‌شود.

برای بررسی یک API Key این endpoint وجود دارد:

```http
GET /auth/whoami
X-Api-Key: shn_...
Authorization: Bearer <access-token>
```

در وضعیت فعلی، به‌دلیل policy عمومی پنل، `whoami` علاوه بر `X-Api-Key` به Bearer Token هم نیاز دارد. این موضوع باید هنگام تکمیل بخش Login بازبینی شود.

### 5.3 رفتار فعلی endpoint ارسال

`POST /messages` از policy عمومی JWT مستثنا شده و خودش `X-Api-Key` را اعتبارسنجی می‌کند. پس برای تست مستقیم ارسال، Bearer Token لازم نیست؛ یک API Key معتبر کافی است.

### 5.4 پیشنهاد ساده برای مدیریت token در React

- Access token پنل را در حافظه برنامه نگه دارید.
- افزودن `Authorization` را در یک API client مرکزی انجام دهید، نه داخل تک‌تک کامپوننت‌ها.
- API Key را فقط برای درخواست‌هایی که واقعاً به هویت مشتری نیاز دارند ارسال کنید.
- روی `401` کاربر را به صفحه ورود برگردانید؛ روی `403` پیام نداشتن دسترسی نمایش دهید.
- قرارداد Login را بعد از اضافه شدن endpoint واقعی از OpenAPI تولید یا پیاده‌سازی کنید.

## 6. شروع پروژه React

پیشنهاد ساده برای این پروژه React + TypeScript است. ابتدا یک API client کوچک بسازید و همه درخواست‌ها را از آن عبور دهید.

```ts
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/backend";

type ApiRequestOptions = RequestInit & {
  accessToken?: string;
  apiKey?: string;
};

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<ApiResponse<T>> {
  const { accessToken, apiKey, headers, ...requestOptions } = options;

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...requestOptions,
    headers: {
      ...(requestOptions.body ? { "Content-Type": "application/json" } : {}),
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...(apiKey ? { "X-Api-Key": apiKey } : {}),
      ...headers,
    },
  });

  const result = (await response.json()) as ApiResponse<T>;

  if (!response.ok || !result.success) {
    throw result;
  }

  return result;
}
```

### اجرای محلی و CORS

بک‌اند دارای CORS قابل تنظیم است و در تنظیمات پیش‌فرض، origin زیر اجازه دسترسی دارد:

```text
http://localhost:5173
```

بنابراین React می‌تواند مستقیماً با `VITE_API_BASE_URL=http://localhost:8080` به API متصل شود. اگر پورت یا دامنه فرانت متفاوت است، آن origin باید در `Cors:AllowedOrigins` فایل تنظیمات بک‌اند اضافه و برنامه restart شود:

```json
{
  "Cors": {
    "Enabled": true,
    "AllowedOrigins": [
      "http://localhost:5173",
      "https://panel.example.com"
    ],
    "AllowedMethods": [ "GET", "POST", "PUT", "DELETE" ],
    "AllowedHeaders": [ "Accept", "Authorization", "Content-Type", "X-Api-Key" ],
    "AllowCredentials": false,
    "PreflightMaxAgeSeconds": 600
  }
}
```

هر origin باید شامل protocol و در صورت نیاز port باشد و نباید path داشته باشد. برای مثال `https://panel.example.com` صحیح و `https://panel.example.com/app` نادرست است. استفاده از `*` نیز عمداً پشتیبانی نمی‌شود تا APIهای دارای credential ناخواسته برای همه وب‌سایت‌ها باز نشوند.

اگر ترجیح می‌دهید در توسعه درخواست‌ها هم‌origin دیده شوند، همچنان می‌توانید از proxy خود Vite استفاده کنید:

```ts
// vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/backend": {
        target: "http://localhost:8080",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/backend/, ""),
      },
    },
  },
});
```

در استقرار نهایی فقط دامنه‌های واقعی و قابل اعتماد فرانت‌اند را در `AllowedOrigins` قرار دهید. چون احراز هویت فعلی با headerهای `Authorization` و `X-Api-Key` انجام می‌شود، مقدار پیشنهادی `AllowCredentials` همان `false` است؛ این گزینه مربوط به credential مرورگر مانند cookie است، نه header احراز هویت.

## 7. برنامه اجرایی دو هفته‌ای فرانت‌اند

این برنامه برای **۱۰ روز کاری، روزی ۴ ساعت (مجموعاً ۴۰ ساعت)** نوشته شده است. فرض بر این است که برنامه‌نویس برای تولید کد، تست و رفع خطا از یک LLM توانمند مانند Codex استفاده می‌کند، اما بازبینی کد و تأیید رفتار نهایی همچنان بر عهده برنامه‌نویس است.

هدف پایان دو هفته، تحویل یک MVP قابل اجرا شامل ویزارد راه‌اندازی، پوسته پنل، داشبورد سلامت، داده‌های مرجع، مدیریت پایه مشتری و API Key، مسیر ارسال پیامک و پیگیری نتیجه است. پیاده‌سازی کامل همه گزارش‌ها و صفحات مدیریتی پیشرفته در ۴۰ ساعت واقع‌بینانه نیست و در backlog بعد از این برنامه باقی می‌ماند.

### روز ۱ — ایجاد پروژه و اسکلت ویزارد راه‌اندازی (۴ ساعت)

- ساخت پروژه React + TypeScript با Vite؛
- تنظیم RTL، فونت فارسی، theme پایه، lint و قالب‌بندی؛
- ایجاد routing و layout حداقلی؛
- ساخت ساختار ویزارد چندمرحله‌ای با قابلیت رفت‌وبرگشت بین مراحل؛
- تعریف state ویزارد بدون وابستگی مستقیم به کامپوننت‌ها.

**خروجی قابل تحویل:** پروژه با یک فرمان اجرا شود و کاربر بتواند مراحل خالی ویزارد را تا پایان طی کند.

### روز ۲ — تکمیل ویزارد و اتصال به بک‌اند (۴ ساعت)

- مرحله تعیین آدرس API و اعتبارسنجی URL؛
- بررسی `GET /service-info`، `GET /health/live` و `GET /health/ready`؛
- نمایش خطای قابل فهم برای قطع بودن API، CORS یا آماده نبودن SQL Server؛
- مرحله خلاصه تنظیمات و پایان راه‌اندازی؛
- نگهداری فقط تنظیمات غیرحساس؛ API Key یا Secret در storage دائمی ذخیره نشود.

**خروجی قابل تحویل:** ویزارد با یک بک‌اند واقعی ارتباط را بررسی کند و فقط در صورت معتبر بودن تنظیمات اجازه پایان بدهد.

### روز ۳ — زیرساخت مشترک پنل (۴ ساعت)

- ساخت API client مرکزی و مدل‌های `ApiResponse<T>` و خطا؛
- پشتیبانی متمرکز از Bearer Token و `X-Api-Key`؛
- loading، empty state، error boundary، toast و نمایش `traceId`؛
- auth adapter قابل‌تعویض و route guard با mock؛
- تولید یا تطبیق typeها با OpenAPI و حذف typeهای حدسی.

**خروجی قابل تحویل:** یک صفحه نمونه بتواند حالت موفق، loading، empty، `401`، `403` و خطای validation را درست نمایش دهد.

### روز ۴ — داشبورد سلامت نرم‌افزار (۴ ساعت)

- ساخت کارت وضعیت کلی از `GET /health/ready`؛
- نمایش checkهای SQL Server، storage، memory، provider secrets و background workers؛
- رنگ‌بندی مرکزی `healthy`، `degraded` و `unhealthy`؛
- نمایش `checkedAtUtc`، uptime، duration و جزئیات امن هر check؛
- refresh دستی و polling هر ۳۰ تا ۶۰ ثانیه، فقط هنگامی که صفحه visible است؛
- نمایش `GET /dispatch/operations/summary` در بخش جداگانه و نه به‌عنوان health check.

**خروجی قابل تحویل:** داشبورد responsive بدون وابستگی به شکل داخلی exception و با امکان تشخیص سریع مؤلفه مشکل‌دار.

### روز ۵ — داده‌های مرجع (۴ ساعت)

- صفحات یا کامپوننت‌های انتخاب Message Type، Provider و Sender Line؛
- دریافت GeoSection و نمایش استان، شهر و ناحیه به‌شکل وابسته یا tree؛
- cache کوتاه‌مدت داده‌های مرجع و مدیریت refresh؛
- تست loading، empty و خطای API برای این داده‌ها.

**خروجی قابل تحویل:** کامپوننت‌های مرجع قابل استفاده مجدد در فرم ارسال و فیلترها.

### روز ۶ — مشتری و API Key (۴ ساعت)

- فهرست، ایجاد و ویرایش پایه مشتری؛
- فهرست و صدور API Key؛
- dialog نمایش یک‌باره کلید با دکمه کپی و هشدار واضح؛
- فعال/غیرفعال یا revoke کردن کلید مطابق قرارداد واقعی API؛
- اطمینان از عدم ثبت API Key در log، URL و storage دائمی مرورگر.

**خروجی قابل تحویل:** سناریوی ایجاد مشتری و صدور امن یک API Key از ابتدا تا انتها اجرا شود.

### روز ۷ — اعتبار و پیش‌نمایش تعرفه (۴ ساعت)

- نمایش مانده و تراکنش‌های مشتری؛
- فرم افزایش اعتبار با نمایش مبالغ بر حسب ریال؛
- اتصال فرم پیش‌نمایش هزینه به `POST /tariffs/quote`؛
- نمایش encoding، تعداد کاراکتر، segment و هزینه؛
- جلوگیری از محاسبات پولی با floating point نامطمئن.

**خروجی قابل تحویل:** کاربر بتواند اعتبار مشتری را ببیند و پیش از ارسال، هزینه پیام را دریافت کند.

### روز ۸ — فرم ارسال پیامک (۴ ساعت)

- فرم ارسال یک یا چند پیام با حداکثر ۱۰۰۰ گیرنده؛
- انتخاب Sender Line، Message Type و GeoSection؛
- اعتبارسنجی شماره موبایل، متن و شناسه‌های اختیاری؛
- تولید و حفظ `clientBatchId` برای جلوگیری از ارسال تکراری؛
- نمایش خلاصه و تأیید نهایی پیش از `POST /messages`؛
- هدایت به صفحه نتیجه با `batchId` برگشتی.

**خروجی قابل تحویل:** مسیر کامل quote → تأیید → ارسال با LoopbackSmsProvider کار کند.

### روز ۹ — پیگیری بچ و وضعیت عملیات (۴ ساعت)

- صفحه جزئیات بچ، پیام‌ها و timeline رویدادها؛
- polling هر ۲ تا ۵ ثانیه تا رسیدن به وضعیت نهایی؛
- توقف polling در تب پنهان و بعد از وضعیت نهایی؛
- نگاشت مرکزی enumها به متن و رنگ فارسی؛
- نمایش retry فقط در وضعیت‌هایی که API اجازه می‌دهد؛
- افزودن نمای خلاصه صف از endpoint عملیاتی موجود.

**خروجی قابل تحویل:** کاربر از `batchId` تا نتیجه ارسال و تحویل هر پیام را دنبال کند.

### روز ۱۰ — تثبیت، تست و تحویل (۴ ساعت)

- تست componentهای حساس و API client؛
- یک تست end-to-end برای ویزارد و یک تست برای مسیر ارسال؛
- بررسی responsive، RTL، keyboard navigation و contrast؛
- بررسی عدم نشت token، API Key و اطلاعات حساس؛
- رفع warningها، build نسخه production و تکمیل README اجرای فرانت؛
- ثبت موارد باقی‌مانده و قراردادهای مسدودشده بک‌اند در backlog.

**خروجی قابل تحویل:** build تولیدی بدون خطا، تست‌های اصلی سبز و راهنمای اجرای محلی و production.

### تعریف Done برای پایان دو هفته

- ویزارد راه‌اندازی و داشبورد سلامت با API واقعی کار کنند؛
- مسیر اصلی مشتری → API Key → اعتبار/quote → ارسال → پیگیری قابل اجرا باشد؛
- تمام درخواست‌ها از API client مرکزی عبور کنند؛
- صفحه‌ها حالت loading، empty و error داشته باشند؛
- API Key و token در سورس، URL، log یا storage دائمی ناامن قرار نگیرند؛
- build production و تست‌های مسیرهای حیاتی موفق باشند؛
- موارد خارج از محدوده به‌صورت شفاف در backlog ثبت شده باشند.

### خارج از محدوده این دو هفته

- Login و refresh token واقعی تا زمانی که قرارداد بک‌اند آن ارائه نشده است؛
- تمام گزارش‌های تحلیلی و نمودارهای پیشرفته؛
- CRUD کامل Provider Account، Tariff، Provider و سایر داده‌های مدیریتی؛
- نمایش کامل Delivery Report و Inbound Message؛
- قابلیت‌های ظاهری غیرضروری، انیمیشن‌های سنگین و شخصی‌سازی گسترده داشبورد.

## 8. ترتیب تکمیلی پیاده‌سازی صفحات و APIها

### مرحله 1: پوسته برنامه و زیرساخت مشترک

ابتدا این بخش‌ها را بسازید:

- layout اصلی، منو، routing و صفحه خطای عمومی؛
- API client و مدل `ApiResponse<T>`؛
- نمایش loading، empty state و خطاهای `errors`؛
- نگهداری `traceId` برای خطاها؛
- صفحه داشبورد وضعیت سرویس با `GET /health/ready`.

در این مرحله لازم نیست Login فرضی بسازید.

### مرحله 2: احراز هویت پنل

بعد از اضافه شدن API واقعی Login در بک‌اند:

1. صفحه ورود را به endpoint واقعی متصل کنید؛
2. access token را در API client قرار دهید؛
3. refresh/logout را دقیقاً مطابق قرارداد OpenAPI پیاده کنید؛
4. guard مسیرهای مدیریتی را فعال کنید؛
5. خطاهای `401` و `403` را جدا مدیریت کنید.

تا قبل از آن، این مرحله را با یک auth adapter قابل‌تعویض یا mock جلو ببرید.

### مرحله 3: داده‌های مرجع

این APIها معمولاً ورودی dropdownها و filterها هستند و بهتر است زودتر پیاده شوند:

1. `GET /reference-data/message-types`
2. `GET /reference-data/providers`
3. `GET /reference-data/sender-lines`
4. `GET /reference-data/geo-sections`

`GeoSection` یک لیست سلسله‌مراتبی است. ارتباط والد و فرزند با `parentGeoSectionId` مشخص می‌شود و بهتر است در فرانت به‌صورت tree یا dropdown وابسته نمایش داده شود.

### مرحله 4: مشتری و API Key

به این ترتیب جلو بروید:

1. `GET /customers`
2. `POST /customers`
3. `PUT /customers/{id}`
4. `GET /api-keys?customerId={customerId}`
5. `POST /api-keys`
6. `PUT /api-keys/{id}`
7. `DELETE /api-keys/{id}` برای revoke کردن کلید
8. endpointهای `/api-keys/{apiKeyId}/ip-restrictions`

پس از `POST /api-keys` مقدار `data.key` را در یک dialog با هشدار «فقط یک بار نمایش داده می‌شود» نشان دهید و امکان کپی کردن بدهید. بعد از بسته شدن dialog نباید انتظار داشت کلید دوباره از سرور دریافت شود.

### مرحله 5: اعتبار و دفتر مالی

1. `GET /balances?customerId={customerId}`
2. `GET /balances/transactions?customerId={customerId}`
3. `POST /balances/top-up`

مبالغ بر حسب ریال و از نوع decimal هستند. برای پول از محاسبات اعشاری مطمئن استفاده کنید و آن را به عدد ممیز شناور نامطمئن تبدیل نکنید. مقدار نمایشی می‌تواند جداگانه با جداکننده هزارگان فرمت شود.

### مرحله 6: تعرفه و پیش‌نمایش هزینه

1. `GET /tariffs`
2. `POST /tariffs/quote`
3. سپس CRUD تعرفه‌ها برای صفحه مدیریت

در فرم ارسال، قبل از ثبت نهایی می‌توانید متن را به `POST /tariffs/quote` بدهید تا encoding، تعداد کاراکتر، تعداد بخش و هزینه محاسبه شود. متن فارسی معمولاً با UCS-2 محاسبه می‌شود.

### مرحله 7: فرم ارسال پیامک

فرم حداقل این فیلدها را نیاز دارد:

- API Key تست یا هویت مشتری انتخاب‌شده؛
- شماره فرستنده (`senderLine`)؛
- نوع پیام (`messageTypeId`)؛
- شناسه اختیاری بچ برای idempotency (`clientBatchId`)؛
- یک یا چند گیرنده؛
- متن مستقل هر گیرنده؛
- محدوده جغرافیایی اختیاری؛
- `clientCorrelatedId`، `billId` و `payId` اختیاری.

هر درخواست حداکثر 1000 پیام دارد. شماره موبایل باید یکی از این دو شکل باشد:

```text
09123456789
989123456789
```

### مرحله 8: پیگیری بچ و پیام‌ها

پس از ارسال، `batchId` را ذخیره و این APIها را پیاده کنید:

1. `GET /batches/{id}` — وضعیت کلی بچ؛
2. `GET /batches/{id}/messages` — وضعیت تک‌تک پیام‌ها؛
3. `GET /batches/{id}/events` — timeline عملیاتی؛
4. `POST /batches/{id}/retry-dispatch` — تلاش مجدد دستی در حالت مجاز.

ارسال غیرهم‌زمان است. برای صفحه جزئیات بچ می‌توان تا رسیدن به وضعیت نهایی هر 2 تا 5 ثانیه polling انجام داد و وقتی صفحه در پس‌زمینه است polling را متوقف کرد.

وضعیت‌های نهایی بچ عبارت‌اند از:

- `3 = DispatchCompleted`
- `4 = DispatchPartiallyFailed`
- `6 = Rejected`
- `7 = DispatchFailed`

`1 = Received`، `2 = Dispatching` و `5 = Held` نهایی نیستند. مقدار `Held` یعنی بچ فعلاً متوقف شده و ممکن است بعداً دوباره وارد صف شود.

enumهای فعلی در JSON به‌صورت عدد فرستاده می‌شوند. در فرانت‌اند برای آن‌ها type و map مرکزی بسازید و اعداد را مستقیم در کامپوننت‌ها پخش نکنید. نگاشت‌های اصلی:

```ts
export const batchStatusLabel: Record<number, string> = {
  1: "دریافت‌شده",
  2: "در حال ارسال",
  3: "ارسال کامل شد",
  4: "بخشی ناموفق بود",
  5: "متوقف‌شده",
  6: "ردشده",
  7: "ارسال ناموفق",
};

export const sendStatusLabel: Record<number, string> = {
  1: "در صف",
  2: "تحویل‌شده به ارائه‌دهنده",
  3: "ارسال‌شده",
  4: "ردشده",
  5: "نامشخص",
  6: "در انتظار تأیید",
};

export const deliveryStatusLabel: Record<number, string> = {
  1: "در انتظار گزارش",
  2: "تحویل‌شده",
  3: "تحویل‌نشده",
  4: "منقضی‌شده",
  5: "نامشخص",
};
```

### مرحله 9: گزارش‌ها

پس از کامل شدن مسیر اصلی ارسال و پیگیری، گزارش‌ها را اضافه کنید:

1. `GET /reports/messages/summary`
2. `GET /reports/messages/by-provider`
3. `GET /reports/messages/by-message-type`
4. `GET /reports/messages/by-geo`
5. `GET /reports/messages/by-jalali-month`
6. `GET /reports/messages/by-geo-rollup`
7. `GET /reports/messages/by-provider-message-type-geo`

تاریخ‌های گزارش شمسی و با قالب ثابت زیر هستند:

```text
1405/04/24
```

### مرحله 10: امکانات مدیریتی پیشرفته

این بخش‌ها را بعد از مسیر اصلی محصول بسازید:

- `GET /dispatch/operations/summary`
- `GET /dispatch/operations/batches`
- مدیریت `provider-accounts`
- CRUD ارائه‌دهندگان، خطوط ارسال، انواع پیام و مناطق جغرافیایی
- `GET /delivery-reports`
- `GET /inbound-messages`

## 9. سناریوی کامل ارسال یک پیامک آزمایشی

### پیش‌نیازهای بک‌اند

برای پذیرفته شدن درخواست باید این داده‌ها از قبل وجود داشته باشند:

1. یک مشتری فعال؛
2. یک API Key فعال برای همان مشتری؛
3. یک خط فرستنده فعال و قابل استفاده برای مشتری؛
4. یک تعرفه فعال برای provider و encoding متن؛
5. اعتبار ریالی کافی برای مشتری؛
6. در ارسال واقعی، provider account معتبر و اتصال آن به خط فرستنده.

نوع‌های پیام و providerهای اولیه هنگام migration ساخته می‌شوند. اگر provider واقعی در تنظیمات فعال نباشد، بک‌اند از `LoopbackSmsProvider` استفاده می‌کند؛ در این حالت مسیر نرم‌افزار آزمایش می‌شود اما پیامک واقعی به گوشی ارسال نخواهد شد.

### 9.1 ساخت داده‌های تست توسط مدیر

این درخواست‌ها مدیریتی‌اند و Bearer Token معتبر می‌خواهند. قرارداد دقیق را از Scalar بردارید.

ساخت مشتری:

```http
POST /customers
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "name": "مشتری آزمایشی",
  "code": "frontend-test"
}
```

صدور API Key؛ کلید برگشتی را همان لحظه ذخیره کنید:

```http
POST /api-keys
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "customerId": 1,
  "name": "frontend-development",
  "expiresAtUtc": null
}
```

افزایش اعتبار:

```http
POST /balances/top-up
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "customerId": 1,
  "amount": 1000000,
  "reference": "frontend-test-credit"
}
```

ساخت یک خط اشتراکی برای تست نرم‌افزاری با provider شماره 1:

```http
POST /reference-data/sender-lines
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "providerId": 1,
  "lineNumber": "30001234",
  "isSharedLine": true,
  "customerId": null,
  "providerAccountId": null,
  "isActive": true
}
```

ساخت یک تعرفه ساده برای متن فارسی (`encoding: 1` یعنی UCS-2):

```http
POST /tariffs
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "providerId": 1,
  "messageTypeId": 2,
  "encoding": 1,
  "effectiveFromUtc": "2026-01-01T00:00:00Z",
  "effectiveToUtc": null,
  "isActive": true,
  "rates": [
    {
      "minChars": 1,
      "maxChars": null,
      "pricePerSegment": 1000
    }
  ]
}
```

این نمونه برای تست مسیر `LoopbackSmsProvider` کافی است. اگر قرار است پیامک واقعی ارسال شود، ابتدا provider account واقعی ساخته و به sender line متصل شود و provider مربوطه نیز در تنظیمات برنامه فعال باشد.

### 9.2 بررسی قیمت

با Bearer Token پنل:

```http
POST /tariffs/quote
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "providerId": 1,
  "messageTypeId": 2,
  "text": "این یک پیامک آزمایشی از SmsHubNext است."
}
```

### 9.3 ارسال پیام

درخواست ارسال فقط به API Key مشتری نیاز دارد:

```http
POST /messages
X-Api-Key: shn_REPLACE_WITH_TEST_KEY
Content-Type: application/json

{
  "senderLine": "30001234",
  "messageTypeId": 2,
  "clientBatchId": "frontend-test-0001",
  "messages": [
    {
      "recipient": "09123456789",
      "text": "این یک پیامک آزمایشی از SmsHubNext است.",
      "clientCorrelatedId": "frontend-message-0001",
      "billId": null,
      "payId": null,
      "geoSectionId": null
    }
  ]
}
```

فیلد قدیمی `customerId` در بدنه ارسال لازم نیست و نباید مبنای هویت قرار بگیرد؛ مشتری از روی API Key تشخیص داده می‌شود.

نمونه پاسخ پذیرش:

```json
{
  "success": true,
  "code": "accepted",
  "message": "Accepted.",
  "data": {
    "batchId": 123,
    "acceptedCount": 1,
    "isDuplicate": false
  },
  "meta": {
    "traceId": "...",
    "timestampUtc": "2026-07-15T09:00:00Z"
  }
}
```

اگر همان `clientBatchId` با همان payload دوباره ارسال شود، پاسخ می‌تواند همان بچ قبلی را با `isDuplicate: true` برگرداند. از تغییر payload و استفاده دوباره از همان شناسه خودداری کنید.

نمونه فراخوانی از React:

```ts
type SendMessagesResult = {
  batchId: number;
  acceptedCount: number;
  isDuplicate: boolean;
};

const result = await apiRequest<SendMessagesResult>("/messages", {
  method: "POST",
  apiKey: testApiKey,
  body: JSON.stringify({
    senderLine: "30001234",
    messageTypeId: 2,
    clientBatchId: crypto.randomUUID(),
    messages: [
      {
        recipient: "09123456789",
        text: "این یک پیامک آزمایشی از SmsHubNext است.",
        clientCorrelatedId: crypto.randomUUID(),
        billId: null,
        payId: null,
        geoSectionId: null,
      },
    ],
  }),
});

const batchId = result.data!.batchId;
```

### 9.4 پیگیری نتیجه

با `batchId` برگشتی:

```http
GET /batches/123
Authorization: Bearer <access-token>
```

و برای وضعیت پیام:

```http
GET /batches/123/messages
Authorization: Bearer <access-token>
```

در هر پیام دو وضعیت مستقل وجود دارد:

- `status`: وضعیت ارسال به provider؛
- `deliveryStatus`: وضعیت تحویل به گوشی مقصد.

رسیدن `status` به `3 = Sent` الزاماً به معنی تحویل نیست. تحویل نهایی زمانی است که `deliveryStatus` برابر `2 = Delivered` شود.

## 10. چک‌لیست تحویل نسخه اول فرانت‌اند

- صفحه سلامت سرویس و مدیریت خطای اتصال؛
- API client مرکزی با پشتیبانی Bearer و API Key؛
- نمایش استاندارد خطاها و `traceId`؛
- پوسته Login آماده اتصال به قرارداد واقعی، بدون endpoint فرضی؛
- صفحات داده‌های مرجع؛
- مشتریان، API Key و اعتبار؛
- تعرفه و پیش‌نمایش هزینه؛
- فرم ارسال یک یا چند پیام؛
- صفحه نتیجه ارسال با `batchId`؛
- پیگیری بچ، پیام‌ها و timeline؛
- گزارش خلاصه پیام‌ها؛
- عدم ذخیره API Key در سورس یا storage دائمی مرورگر.

## 11. منابع تکمیلی داخل مخزن

- `README.md` — مدل دامنه و ساختار داده؛
- `ARCHITECTURE.md` — معماری نرم‌افزار و قواعد بک‌اند؛
- `docs/diagrams/001-2026-07-04-sms-send-lifecycle.md` — چرخه کامل ارسال پیام؛
- `docs/providers/magfa-http-v2.md` — جزئیات اتصال مگفا؛
- `docs/providers/kavenegar-rest.md` — جزئیات اتصال کاوه‌نگار؛
- `docs/operations/first-deployment-fa.md` — استقرار اولیه؛
- `docs/operations/application-configuration-guide.md` — تنظیمات برنامه.
- `docs/operations/health-checks-fa.md` — قرارداد خروجی سلامت برای داشبورد.

برای توسعه روزمره فرانت‌اند، Scalar مرجع اصلی قرارداد HTTP است و این سند مرجع ترتیب و منطق پیاده‌سازی محسوب می‌شود.
