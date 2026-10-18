# اجرای محلی برنامه یکپارچه

تنها پروژه قابل اجرا `src/SmsHubNext/SmsHubNext.csproj` است. پنل Blazor، API، دیتابیس و سرویس‌های پس‌زمینه همگی در همین میزبان قرار دارند.

1. تنظیم اتصال SQL Server را در فایل محلی `src/SmsHubNext/appsettings.Development.local.json` قرار دهید. این فایل در Git ثبت نمی‌شود. تنظیمات نصب و اتصال در [راهنمای اولین استقرار](first-deployment-fa.md) آمده است.
2. در Visual Studio فقط پروژه `SmsHubNext` را به‌عنوان Startup Project انتخاب کنید، یا از ریشه مخزن اجرا کنید:

   ```powershell
   dotnet run --project src/SmsHubNext
   ```

3. صفحه اصلی را در `http://localhost:5080` باز کنید. صفحه ورود `/login` و داشبورد `/admin/dashboard` است. در محیط Development حساب آزمایشی `Admin` با رمز `123456` فعال است؛ ورود آزمایشی در محیط Production فعال نیست.

تنظیمات `DevelopmentSeed:Enabled` و کلیدهای آزمایشی موجود در `appsettings.Development.json` برای داده‌های توسعه هستند. اگر seed غیرفعال است، کلیدهای معتبر پایگاه داده خود را در فایل محلی و بخش `ManagementUi:AdminApiKey` و `ManagementUi:CustomerApiKey` قرار دهید. کلید مدیریتی باید سطح دسترسی Administrator داشته باشد؛ کلید ارسال باید متعلق به مشتری باشد.

پنل مستقیماً همان handlerهای داخلی برنامه را فراخوانی می‌کند. تنظیم `SmsHubApi:BaseUrl`، تبادل JWT برای درخواست‌های داخلی پنل و اجرای برنامه دوم حذف شده‌اند. کلیدها همچنان برای بررسی وضعیت فعال/انقضا/ابطال، محدودیت IP و انتساب عملیات به عامل معتبر استفاده می‌شوند. مهلت پیش‌فرض عملیات پنل ۱۵ ثانیه است و با `ManagementUi:OperationTimeoutSeconds` قابل تنظیم است.

API بیرونی همچنان مسیرهای قبلی را دارد: برای نمونه `/customers` مسیر API و `/admin/customers` صفحه پنل است. دسترسی مدیریتی API همچنان JWT می‌خواهد؛ کوکی ورود پنل جایگزین آن نمی‌شود. ارسال پیامک بیرونی همچنان با کلید مشتری انجام می‌شود.
