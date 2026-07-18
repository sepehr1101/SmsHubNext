# بازگشت به وضعیت نصب اولیه

## API

```http
POST /setup/factory-reset
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "confirmation": "RESET"
}
```

این endpoint مخرب است و مقدار `confirmation` باید دقیقاً `RESET` باشد. پاسخ موفق اعلام می‌کند که ویزارد باید دوباره نمایش داده شود:

```json
{
  "success": true,
  "code": "ok",
  "data": {
    "resetAtUtc": "2026-07-18T08:30:00Z",
    "requiresSetupWizard": true
  }
}
```

## شرط غیرقابل اغماض

اگر حتی یک ردیف در جدول `Message` وجود داشته باشد، عملیات با `409 Conflict` و کد زیر رد می‌شود و هیچ داده‌ای تغییر نمی‌کند:

```text
setup.factory_reset_messages_exist
```

بررسی نبود پیام و پاک‌سازی در یک transaction انجام می‌شوند. مسیر پذیرش پیام و factory reset از یک application lock در SQL Server استفاده می‌کنند؛ بنابراین پیام جدید نمی‌تواند در فاصله بررسی و commit شدن reset ثبت شود.

## مواردی که پاک می‌شوند

- مشتریان، API Keyها و محدودیت‌های IP؛
- مانده‌ها و تمام تراکنش‌های مالی؛
- تعرفه‌ها و نرخ‌ها؛
- خطوط ارسال و Provider Accountها؛
- جغرافیا؛
- batchها، eventها، داده‌های inbound و صف‌های عملیاتی؛
- تمام تغییرات انجام‌شده روی Provider و Message Type.

پس از reset هیچ Provider، Message Type یا داده کسب‌وکاری دیگری seed نمی‌شود. identityهای جداول reset می‌شوند و ویزارد باید تمام اطلاعات نصب را دوباره از کاربر دریافت کند.

## مواردی که عمداً حفظ می‌شوند

- خود schema دیتابیس و journal مربوط به DbUp؛
- connection string؛
- تنظیمات میزبانی، CORS، JWT و endpointهای Provider در فایل‌های `appsettings`؛
- Data Protection Key Ring؛
- فایل‌های برنامه و تنظیمات IIS.

این موارد زیرساخت لازم برای روشن ماندن API و اجرای دوباره ویزارد هستند و داده کسب‌وکاری نصب محسوب نمی‌شوند. API process را restart نمی‌کند؛ پس از پاسخ موفق، فرانت‌اند باید state ویزارد خود را پاک و کاربر را به ابتدای ویزارد هدایت کند.
