# راهنمای نصب ویندوز SmsHubNext

این راهنما مرجع نصب، ارتقا، حذف و عیب‌یابی بستهٔ ویندوز SmsHubNext است. اینستالر برای کاربر غیرفنی طراحی شده، اما نقش آن عمداً محدود به استقرار است؛ تنظیمات کسب‌وکاری بعد از نصب در رابط وب آینده انجام می‌شوند.

## تصمیم معماری

بسته با **Inno Setup 6.7.3** ساخته می‌شود و برای هماهنگی عملیات ویندوز از چند اسکریپت PowerShell کوچک استفاده می‌کند. برنامهٔ WinForms/WPF جداگانه نداریم. منطق حساس به ساخت connection string و نوشتن تنظیمات production در command mode همان executable اصلی SmsHubNext قرار دارد تا اینستالر به backend دوم تبدیل نشود. اجرای migration فقط در مسیر عادی startup برنامهٔ وب (`app.MigrateDatabase()`) انجام می‌شود؛ setup command هیچ migration مستقلی ندارد.

این مرزبندی عمداً چنین است:

- Inno: صفحات نصب، کپی فایل‌ها، ارتقا، حذف و اجرای مراحل
- PowerShell: IIS، Windows Features، Hosting Bundle، ACL، firewall و health check
- SmsHubNext setup command: اعتبارسنجی اتصال، ساخت امن connection string و ساخت/ادغام `appsettings.Production.json`
- startup عادی برنامهٔ وب: ساخت دیتابیس در صورت نبودن و اجرای DbUp migration
- رابط React آینده: customer، API key، provider account، sender line، tariff، geography و سایر setupهای کسب‌وکاری

پلتفرم هدف Windows Server 2019 یا جدیدتر، x64 است. Windows 10/11 Pro یا Enterprise دارای IIS برای محیط آزمایش قابل استفاده است؛ Windows Home هدف پشتیبانی نیست.

## کاری که اینستالر انجام می‌دهد

1. دسترسی Administrator و سازگاری Windows x64 را بررسی می‌کند.
2. IIS و اجزای لازم آن را فعال می‌کند و اگر سرویس‌های `WAS` یا `W3SVC` متوقف باشند، پیش از استقرار آن‌ها را راه‌اندازی می‌کند.
3. وجود ASP.NET Core Module و runtime نسخهٔ 10 را بررسی می‌کند و در صورت نیاز Hosting Bundle رسمیِ داخل بسته را بدون اینترنت نصب می‌کند.
4. نام سایت، پورت و host name اختیاری را می‌گیرد؛ دکمه «بررسی پورت» و کنترل اجباری هنگام ادامه، آزادبودن binding را پیش از نصب بررسی می‌کنند.
5. مشخصات SQL Server را می‌گیرد و اتصال را آزمایش می‌کند. SQL Server می‌تواند محلی یا روی سرور دیگری باشد.
6. `appsettings.Production.json` را به‌صورت atomic می‌سازد یا با حفظ تنظیمات نامرتبط به‌روزرسانی می‌کند.
7. در اولین نصب یک JWT signing key تصادفی قوی تولید می‌کند و Data Protection key ring را در `%ProgramData%\SmsHubNext` قرار می‌دهد.
8. app pool، سایت IIS، binding، دسترسی فایل‌ها و firewall rule را تنظیم می‌کند.
9. سایت را اجرا می‌کند؛ خود برنامه در startup دیتابیس ناموجود را می‌سازد و migrationها را اجرا می‌کند.
10. `/health` را تا ۶۰ ثانیه بررسی می‌کند تا موفقیت startup و readiness dependencyهای ضروری تأیید شود.

اینستالر عمداً این کارها را انجام **نمی‌دهد**:

- SQL Server را دانلود، جست‌وجو یا نصب نمی‌کند.
- گواهی TLS/HTTPS صادر یا binding آن را مدیریت نمی‌کند؛ نسخهٔ اول روی HTTP نصب می‌شود.
- دیتابیس، تنظیمات production، لاگ‌ها یا Data Protection keys را هنگام uninstall حذف نمی‌کند.
- داده‌های کسب‌وکاری اولیه را حدس نمی‌زند.
- از اینترنت سرور مقصد چیزی دریافت نمی‌کند.

## نصب تعاملی

فایل `SmsHubNext-Setup-<version>-x64.exe` را با یک حساب Administrator اجرا کنید. مقادیر پیشنهادی برای نصب معمولی:

- Site name: `SmsHubNext`
- Port: `8080`
- Host name: خالی
- Authentication: `SQL Server Authentication`
- Database: `SmsHubNext`

پس از نصب، میان‌بر SmsHubNext صفحه شروع فارسی سرویس را باز می‌کند. بررسی سلامت از `/health` و اطلاعات فنی JSON از `/service-info` در دسترس است.
Installer برای جلوگیری از خوابیدن workerهای SQL-backed، App Pool را `AlwaysRunning` نگه می‌دارد، idle timeout را صفر می‌کند و Application Initialization/Preload را فعال می‌کند.

رمز SQL در command line قرار نمی‌گیرد. اینستالر آن را در یک response file موقت UTF-8 در `%TEMP%` می‌نویسد و در پایان یا شکست حذف می‌کند. connection string نهایی طبق تصمیم فعلی پروژه به‌صورت plaintext در `appsettings.Production.json` ذخیره می‌شود، ولی ACL فایل به `SYSTEM`، Administrators و app pool محدود می‌شود.

`Encrypt=True` همیشه فعال است. برای ساده ماندن نصب در شبکه‌های داخلی، مقدار پیش‌فرض `TrustServerCertificate=True` است؛ یعنی کانال رمز می‌شود ولی هویت certificate سرور SQL به‌طور کامل اعتبارسنجی نمی‌شود. در محیطی با PKI درست، response file اتوماتیک می‌تواند این مقدار را `false` کند.

### Windows Authentication

این حالت پیشرفته است. تست اولیهٔ اتصال با هویت Administrator اجرا می‌شود، اما migration و اجرای برنامه با هویت `IIS AppPool\SmsHubNext` انجام می‌شوند. برای SQL محلی باید login مناسب این هویت ساخته شود؛ برای SQL راه‌دور معمولاً باید machine account دامنه یا یک service account از قبل در SQL مجاز شده باشد. health check نهایی اختلاف مجوز را آشکار می‌کند، اما برای نصب ساده SQL Authentication انتخاب پیشنهادی است.

## نصب silent

در نصب silent واردکردن secret در command line ممنوع است. ابتدا response file زیر را در مسیری با دسترسی محدود بسازید:

```json
{
  "server": "sql01.example.local,1433",
  "database": "SmsHubNext",
  "authentication": "SqlServer",
  "username": "smshub_setup",
  "password": "REPLACE_ME",
  "connectTimeoutSeconds": 15,
  "trustServerCertificate": true
}
```

سپس:

```powershell
.\SmsHubNext-Setup-0.1.6-x64.exe `
  /VERYSILENT `
  /SUPPRESSMSGBOXES `
  /NORESTART `
  /SETUPREQUEST="C:\SecureTemp\smshub-setup.json" `
  /LOG="C:\SecureTemp\smshub-install.log"
```

فایل ورودی متعلق به اپراتور است و اینستالر اصل آن را حذف نمی‌کند؛ بعد از نصب آن را امن حذف کنید. نبودن `/SETUPREQUEST` در نصب silent باعث توقف کنترل‌شده می‌شود. پارامترهای IIS در نسخهٔ فعلی از مقدار قبلی یا default استفاده می‌کنند.

## ارتقا و rollback

AppId اینستالر ثابت است؛ اجرای نسخهٔ جدید upgrade همان نصب محسوب می‌شود. در ارتقا:

1. اتصال production موجود به‌طور پیش‌فرض حفظ می‌شود؛ کاربر می‌تواند آگاهانه آن را تغییر دهد.
2. سایت متوقف و یک backup موقت از فایل‌های نسخهٔ قبلی گرفته می‌شود.
3. فایل‌های جدید نصب می‌شوند و سایت بالا می‌آید؛ مسیر عادی startup برنامه migrationهای forward-only را اجرا می‌کند.
4. IIS و health check بررسی می‌شوند.
5. فقط پس از سلامت کامل، backup موقت حذف می‌شود.

اگر configuration، startup، IIS یا health check شکست بخورد، فایل‌های نسخهٔ قبلی و binding قبلی بازگردانده می‌شوند. migrationهایی که خود برنامه در startup اجرا کرده است برگشت داده نمی‌شوند؛ schema طبق قرارداد پروژه باید additive و با نسخهٔ قبلی سازگار باشد. پیش از ارتقای production همچنان backup مستقل SQL Server لازم است.

در شکست نصب تازه، سایت/app pool/firewall ایجادشده پاک می‌شود. دیتابیس احتمالی حذف نمی‌شود تا هیچ داده‌ای به‌صورت خودکار نابود نشود.

## حذف

Uninstall سایت، app pool، firewall rule و فایل‌های مدیریت‌شدهٔ برنامه را حذف می‌کند. موارد زیر عمداً باقی می‌مانند:

- دیتابیس SQL Server
- `appsettings.Production.json` تولیدشده، اگر خارج از فهرست فایل‌های مدیریت‌شده باقی بماند
- `%ProgramData%\SmsHubNext\DataProtection-Keys`
- `%ProgramData%\SmsHubNext\Logs`

حذف نهایی این داده‌ها یک عملیات جدا و آگاهانهٔ مدیریتی است.

## عیب‌یابی

- لاگ نصب: با `/LOG=<path>` مسیر مشخص کنید؛ Inno به‌صورت پیش‌فرض نیز setup logging دارد.
- نتیجهٔ شکست SQL: آدرس سرور، firewall، فعال بودن TCP، authentication mode و مجوز ساخت/دسترسی دیتابیس را بررسی کنید.
- خطای Hosting Bundle: اگر نصب prerequisite کد restart بدهد، Windows را restart و همان setup را دوباره اجرا کنید.
- خطای فعال‌سازی IIS: روی سروری که payload مربوط به Windows Features حذف شده است ممکن است Windows Update، WSUS یا installation media ویندوز لازم باشد؛ خود این payload قابل بسته‌بندی قانونی/عملی داخل installer برنامه نیست.
- خطای health: Windows Event Viewer، IIS logs و `%ProgramData%\SmsHubNext\Logs` را بررسی کنید.
- هشدار SmartScreen: artifact فعلی code-sign نشده است. SHA-256 منتشرشده کنار release را قبل از اجرا مقایسه کنید.

## معیار پذیرش release

هر release اینستالر باید این ماتریس را روی VM disposable پاس کند:

| سناریو | انتظار |
|---|---|
| Windows Server 2019 بدون IIS و بدون Hosting Bundle | prerequisiteها نصب، restart در صورت نیاز، نصب مجدد موفق |
| SQL محلی موجود، دیتابیس ناموجود | setup فقط config را بنویسد؛ startup وب دیتابیس را بسازد و migration/health موفق شوند |
| SQL راه‌دور | فقط تست اتصال؛ هیچ جست‌وجو/نصب SQL روی سرور برنامه انجام نشود |
| رمز دارای فاصله، کوتیشن و `;` | اتصال موفق و secret در process arguments/log ظاهر نشود |
| ارتقای سالم | config و Data Protection keys حفظ شوند |
| شکست عمدی health در upgrade | binary/config/binding قبلی بازگردند |
| uninstall | IIS resources حذف و DB/key ring/log حفظ شوند |
| silent بدون response file | نصب قبل از تغییر برنامه متوقف شود |
