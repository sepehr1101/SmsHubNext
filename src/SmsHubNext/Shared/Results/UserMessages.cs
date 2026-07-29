namespace SmsHubNext.Shared.Results;

/// <summary>
/// Persian user-facing API messages. Keep stable error codes near call sites, but centralize text
/// here so wording changes do not require hunting through handlers.
/// </summary>
public static class UserMessages
{
    public static class Common
    {
        public const string RequestInvalid = "درخواست نامعتبر است.";
        public const string UnexpectedError = "خطای غیرمنتظره‌ای رخ داد.";
        public const string DatabaseUnavailable = "پایگاه داده موقتاً در دسترس نیست.";
        public const string BadRequest = "امکان پردازش درخواست وجود ندارد.";
        public const string Timeout = "مهلت پردازش درخواست به پایان رسید.";
        public const string OperationCancelled = "عملیات پیش از تکمیل لغو شد.";
        public static string InvalidField(string field) =>
            string.IsNullOrWhiteSpace(field)
                ? "یکی از مقادیر درخواست معتبر نیست."
                : $"مقدار فیلد «{field}» معتبر نیست.";
    }

    public static class Auth
    {
        public const string MissingKey = "کلید API الزامی است.";
        public const string InvalidKey = "کلید API معتبر نیست.";
        public const string InactiveKey = "کلید API غیرفعال یا لغو شده است.";
        public const string ExpiredKey = "اعتبار کلید API به پایان رسیده است.";
        public const string IpNotAllowed = "نشانی IP درخواست‌دهنده برای این کلید API مجاز نیست.";
    }

    public static class ApiKeys
    {
        public const string UnknownCustomer = "مشتری موردنظر وجود ندارد.";
        public const string CustomerRequired = "شناسه معتبر مشتری الزامی است.";
        public const string NameRequired = "نام کلید API الزامی است.";
        public const string UnknownKey = "کلید API موردنظر وجود ندارد.";
        public const string CidrRequired = "محدوده CIDR الزامی است.";
        public const string InvalidId = "شناسه معتبر کلید API یا محدودیت IP الزامی است.";
        public const string NotFound = "کلید API موردنظر وجود ندارد.";
        public const string RevokedCannotChange = "کلید API لغوشده قابل تغییر نیست.";
        public const string AlreadyRevoked = "این کلید API قبلاً لغو شده است.";
        public const string IpRestrictionNotFound = "محدودیت IP موردنظر وجود ندارد.";
    }

    public static class Balances
    {
        public const string UnknownCustomer = "مشتری موردنظر وجود ندارد.";
        public const string CustomerRequired = "شناسه معتبر مشتری الزامی است.";
        public const string AmountPositive = "مبلغ افزایش اعتبار باید بیشتر از صفر باشد.";
        public const string ReferenceTooLong = "شناسه پیگیری افزایش اعتبار حداکثر می‌تواند ۱۰۰ نویسه باشد.";
        public const string DuplicateReference = "افزایش اعتباری با این شناسه پیگیری قبلاً ثبت شده است.";
    }

    public static class Batches
    {
        public const string InvalidId = "شناسه معتبر بچ الزامی است.";
        public const string NotFound = "بچ موردنظر وجود ندارد.";
        public const string RetryNotAllowed =
            "فقط بچ‌های ناموفق دارای پیام‌های در صف یا در انتظار تأیید را می‌توان دوباره ارسال کرد.";
        public const string InsufficientBalanceForRetry =
            "موجودی مشتری برای تلاش مجدد ارسال این بچ کافی نیست.";
    }

    public static class Sending
    {
        public const string ClientBatchPayloadMismatch =
            "بچی با این شناسه قبلاً ثبت شده، اما محتوای درخواست آن متفاوت است.";
        public const string UnknownCustomer = "مشتری احراز هویت‌شده وجود ندارد یا غیرفعال است.";
        public const string ApiKeyCustomerMismatch = "کلید API به مشتری احراز هویت‌شده تعلق ندارد.";
        public const string UnknownMessageType = "نوع پیام موردنظر وجود ندارد.";
        public const string UnknownGeoSection = "یک یا چند محدوده جغرافیایی وجود ندارند یا غیرفعال‌اند.";
        public const string UnknownSenderLine = "خط ارسال موردنظر وجود ندارد.";
        public const string InactiveSenderLine = "خط ارسال غیرفعال است.";
        public const string SenderLineNotAllowed = "خط ارسال به مشتری احراز هویت‌شده اختصاص داده نشده است.";
        public const string ProviderCredentialsRequired =
            "اطلاعات حساب سرویس‌دهنده برای خط ارسال تنظیم نشده است.";
        public const string ProviderAccountInactive = "حساب سرویس‌دهنده خط ارسال غیرفعال است.";
        public const string ProviderSecretRequired =
            "اطلاعات محرمانه رمزگذاری‌شده حساب سرویس‌دهنده خط ارسال ثبت نشده است.";
        public const string NoActiveTariff =
            "تعرفه فعالی متناسب با سرویس‌دهنده خط، نوع پیام و نوع کدگذاری پیامک پیدا نشد.";
        public const string NoTariffRateBand =
            "تعرفه فعال وجود دارد، اما تعداد نویسه‌های پیام در هیچ بازه قیمتی قرار نمی‌گیرد.";
        public const string UnknownReference =
            "مشتری، کلید API، نوع پیام یا محدوده جغرافیایی موردنظر وجود ندارد.";
        public const string DuplicateClientBatchId = "بچی با این شناسه قبلاً ثبت شده است.";
        public const string InsufficientBalance = "موجودی پیش‌پرداخت مشتری برای این بچ کافی نیست.";
        public const string SenderLineRequired = "خط ارسال الزامی است.";
        public const string MessageTypeRequired = "شناسه نوع پیام الزامی است.";
        public const string ClientBatchIdRequired =
            "برای جلوگیری از ثبت پیامک تکراری هنگام تلاش مجدد، شناسه بچ الزامی است.";
        public const string ClientBatchIdTooLong = "شناسه بچ حداکثر می‌تواند ۱۰۰ نویسه باشد.";
        public const string MessagesRequired = "ثبت حداقل یک پیام الزامی است.";
        public const string TooManyMessages = "در هر درخواست حداکثر ۵٬۰۰۰ پیام پذیرفته می‌شود.";
        public static string TooManyMessagesLimit(int maxMessages) =>
            $"هر درخواست حداکثر می‌تواند شامل {maxMessages} پیام باشد.";
        public const string RecipientRequired = "شماره گیرنده برای همه پیام‌ها الزامی است.";
        public static string MissingRecipient(int index) => $"شماره گیرنده پیام ردیف {index} وارد نشده است.";
        public static string InvalidRecipient(int index) => $"شماره موبایل ایرانی پیام ردیف {index} معتبر نیست.";
        public const string TextRequired = "متن همه پیام‌ها الزامی است.";
        public static string MissingText(int index) => $"متن پیام ردیف {index} وارد نشده است.";
        public static string TextTooLong(int index, int maxLength) =>
            $"متن پیام ردیف {index} حداکثر می‌تواند {maxLength} نویسه باشد.";
        public static string ClientCorrelatedIdTooLongAt(int index, int maxLength) =>
            $"شناسه پیگیری پیام ردیف {index} نباید بیشتر از {maxLength} نویسه باشد.";
        public static string BillIdTooLong(int index, int maxLength) =>
            $"شناسه قبض پیام ردیف {index} نباید بیشتر از {maxLength} نویسه باشد.";
        public static string PayIdTooLong(int index, int maxLength) =>
            $"شناسه پرداخت پیام ردیف {index} نباید بیشتر از {maxLength} نویسه باشد.";
    }

    public static class Setup
    {
        public const string FactoryResetConfirmationRequired =
            "برای بازنشانی کارخانه‌ای، عبارت تأیید باید دقیقاً «RESET» باشد.";
        public const string FactoryResetMessagesExist =
            "به‌دلیل ثبت حداقل یک پیامک، بازنشانی کارخانه‌ای دیگر امکان‌پذیر نیست.";
    }

    public static class Reports
    {
        public const string FromJalaliInvalid = "مقدار fromJalali باید با قالب yyyy/MM/dd وارد شود.";
        public const string ToJalaliInvalid = "مقدار toJalali باید با قالب yyyy/MM/dd وارد شود.";
        public const string InvalidRange = "مقدار fromJalali باید قبل از یا برابر با toJalali باشد.";
        public const string CustomerInvalid = "مقدار customerId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string ProviderInvalid = "مقدار providerId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string MessageTypeInvalid = "مقدار messageTypeId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string GeoSectionInvalid = "مقدار geoSectionId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string CustomerMismatch = "مشتری درخواست‌شده با کلید API احراز هویت‌شده مطابقت ندارد.";
    }

    public static class DispatchOperations
    {
        public const string FromJalaliInvalid = "مقدار fromJalali باید با قالب yyyy/MM/dd وارد شود.";
        public const string ToJalaliInvalid = "مقدار toJalali باید با قالب yyyy/MM/dd وارد شود.";
        public const string InvalidRange = "مقدار fromJalali باید قبل از یا برابر با toJalali باشد.";
        public const string CustomerInvalid = "مقدار customerId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string ProviderInvalid = "مقدار providerId در صورت ارسال باید بیشتر از صفر باشد.";
        public const string PageInvalid = "مقدار page باید بیشتر از صفر باشد.";
        public const string TakeInvalid = "مقدار take باید بین ۱ تا ۲۰۰ باشد.";
    }

    public static class Tariffs
    {
        public const string NoRate = "نرخ تعرفه فعال و متناسب با درخواست پیدا نشد.";
        public const string ProviderRequired = "شناسه سرویس‌دهنده الزامی است.";
        public const string TextRequired = "متن پیام الزامی است.";
        public const string EncodingInvalid = "نوع کدگذاری پیامک معتبر نیست.";
        public const string EffectiveFromRequired = "زمان UTC شروع اعتبار تعرفه الزامی است.";
        public const string EffectiveRangeInvalid = "زمان پایان اعتبار باید بعد از زمان شروع اعتبار باشد.";
        public const string RatesRequired = "ثبت حداقل یک نرخ تعرفه الزامی است.";
        public const string RateRangesInvalid =
            "بازه‌های نرخ تعرفه باید از نویسه ۱ پیوسته باشند و با یک بازه بدون سقف پایان یابند.";
        public const string RatePriceInvalid = "مبلغ همه نرخ‌های تعرفه باید بیشتر از صفر باشد.";
        public const string UnknownProvider = "سرویس‌دهنده موردنظر وجود ندارد.";
        public const string UnknownMessageType = "نوع پیام موردنظر وجود ندارد.";
        public const string EffectiveRangeOverlap =
            "یک تعرفه فعال دیگر این سرویس‌دهنده، نوع پیام، کدگذاری و بازه زمانی را پوشش می‌دهد.";
        public const string InvalidId = "شناسه معتبر تعرفه الزامی است.";
        public const string NotFound = "تعرفه موردنظر وجود ندارد.";
    }

    public static class DeliveryReports
    {
        public const string UnknownMessage = "پیام موردنظر وجود ندارد.";
        public const string MessageRequired = "شناسه معتبر پیام الزامی است.";
        public const string InvalidStatus = "وضعیت استانداردشده معتبر نیست.";
    }

    public static class ReferenceData
    {
        public const string MessageTypeExists = "نوع پیامی با این شناسه یا کد قبلاً ثبت شده است.";
        public const string MessageTypeIdRequired = "شناسه نوع پیام باید بزرگ‌تر از صفر باشد.";
        public const string NameRequired = "نام الزامی است.";
        public const string CodeRequired = "کد الزامی است.";
        public const string SenderLineUnknownReference = "سرویس‌دهنده یا مشتری موردنظر وجود ندارد.";
        public const string SenderLineUnknownProvider = "سرویس‌دهنده موردنظر وجود ندارد.";
        public const string SenderLineUnknownCustomer = "مشتری موردنظر وجود ندارد.";
        public const string ProviderRequired = "شناسه سرویس‌دهنده الزامی است.";
        public const string InvalidSenderLine = "شناسه خط ارسال باید بیشتر از صفر باشد.";
        public const string SenderLineNotFound = "خط ارسال موردنظر وجود ندارد.";
        public const string LineNumberRequired = "شماره خط ارسال الزامی است.";
        public const string InvalidCustomer = "شناسه مشتری در صورت ارسال باید بیشتر از صفر باشد.";
        public const string InvalidProviderAccount = "شناسه حساب سرویس‌دهنده در صورت ارسال باید بیشتر از صفر باشد.";
        public const string SharedLineHasOwner = "خط ارسال اشتراکی را نمی‌توان به یک مشتری اختصاص داد.";
        public const string SenderLineUnknownProviderAccount = "حساب سرویس‌دهنده موردنظر وجود ندارد.";
        public const string ProviderAccountProviderMismatch =
            "حساب سرویس‌دهنده باید متعلق به همان سرویس‌دهنده خط ارسال باشد.";
        public const string InactiveProviderAccount = "حساب سرویس‌دهنده غیرفعال است.";
        public const string GeoSectionTypeInvalid = "نوع معتبر محدوده جغرافیایی الزامی است.";
        public const string CustomerCodeExists = "مشتری دیگری با این کد قبلاً ثبت شده است.";
        public const string GeoUnknownParent = "محدوده جغرافیایی والد وجود ندارد.";
        public const string CustomerNameRequired = "نام مشتری الزامی است.";
        public const string CustomerCodeRequired = "کد مشتری الزامی است.";
        public const string ProviderCodeExists = "سرویس‌دهنده دیگری با این کد قبلاً ثبت شده است.";
        public const string ProviderNameRequired = "نام سرویس‌دهنده الزامی است.";
        public const string ProviderCodeRequired = "کد سرویس‌دهنده الزامی است.";
        public const string BaseUrlRequired = "نشانی پایه سرویس الزامی است.";
        public const string CustomerNotFound = "مشتری موردنظر وجود ندارد.";
        public const string InvalidProvider = "شناسه معتبر سرویس‌دهنده الزامی است.";
        public const string ProviderNotFound = "سرویس‌دهنده موردنظر وجود ندارد.";
        public const string InvalidMessageType = "شناسه معتبر نوع پیام الزامی است.";
        public const string MessageTypeNotFound = "نوع پیام موردنظر وجود ندارد.";
        public const string InvalidGeoSection = "شناسه محدوده جغرافیایی باید بیشتر از صفر باشد.";
        public const string GeoSectionNotFound = "محدوده جغرافیایی موردنظر وجود ندارد.";
        public const string GeoSectionHasActiveChildren =
            "محدوده جغرافیایی دارای زیرمجموعه حذف‌نشده را نمی‌توان حذف کرد.";
    }

    public static class Providers
    {
        public static string NotRegistered(string providerCode) =>
            $"پیاده‌سازی سرویس‌دهنده پیامک با کد «{providerCode}» ثبت نشده است.";

        public static string NoMagfaAccountForSenderLine(string senderLine) =>
            $"حساب مگفا برای خط ارسال «{senderLine}» تنظیم نشده است.";

        public static string NoKavenegarAccountForSenderLine(string senderLine) =>
            $"حساب کاوه‌نگار برای خط ارسال «{senderLine}» تنظیم نشده است.";

        public static string NoSmsIrAccountForSenderLine(string senderLine) =>
            $"حساب sms.ir برای خط ارسال «{senderLine}» تنظیم نشده است.";

        public static string MagfaRequestStatus(int status) => $"درخواست مگفا با وضعیت {status} ناموفق بود.";
        public const string MagfaTransport = "هنگام ارتباط HTTP با مگفا خطا رخ داد.";
        public const string MagfaTimeout = "مهلت پاسخ‌گویی مگفا به پایان رسید.";
        public static string MagfaHttpStatus(int statusCode) => $"مگفا کد HTTP {statusCode} را برگرداند.";
        public static string MagfaBadJson(string _) => "پاسخ دریافتی از مگفا قابل خواندن نیست.";
        public const string MagfaEmptyBody = "پاسخ دریافتی از مگفا خالی است.";
        public const string MagfaMissingResult = "مگفا برای این پیام نتیجه‌ای برنگرداند.";
        public const string MagfaMissingId = "مگفا پیام را پذیرفت، اما شناسه‌ای برنگرداند.";
        public static string MagfaMessageStatus(int status) => $"مگفا وضعیت ناشناخته {status} را برای پیام برگرداند.";
        public static string MagfaRejectedStatus(int status) => $"مگفا پیام را با وضعیت {status} رد کرد.";
        public static string MagfaRejectedConfigurationStatus(int status) =>
            $"مگفا درخواست را به‌دلیل خطای پیکربندی با وضعیت {status} رد کرد.";
        public static string MagfaDefinitelyNotSubmitted(int status) =>
            $"مگفا درخواست را پیش از ثبت، به‌دلیل خطای احراز هویت یا حساب با وضعیت {status} رد کرد.";

        public const string KavenegarTransport = "هنگام ارتباط HTTP با کاوه‌نگار خطا رخ داد.";
        public const string KavenegarTimeout = "مهلت پاسخ‌گویی کاوه‌نگار به پایان رسید.";
        public static string KavenegarHttpStatus(int statusCode) => $"کاوه‌نگار کد HTTP {statusCode} را برگرداند.";
        public static string KavenegarBadJson(string _) => "پاسخ دریافتی از کاوه‌نگار قابل خواندن نیست.";
        public const string KavenegarEmptyBody = "پاسخ دریافتی از کاوه‌نگار خالی است.";
        public const string KavenegarMissingResult = "کاوه‌نگار برای این پیام نتیجه‌ای برنگرداند.";
        public const string KavenegarMissingId = "کاوه‌نگار پیام را پذیرفت، اما شناسه‌ای برنگرداند.";
        public static string KavenegarRejectedStatus(int status) => $"کاوه‌نگار پیام را با وضعیت {status} رد کرد.";
        public static string KavenegarMessageStatus(long messageId, int status) =>
            $"کاوه‌نگار برای پیام {messageId} وضعیت ناشناخته {status} را برگرداند.";
        public static string KavenegarRequestStatus(string method, int status, string _) =>
            $"درخواست {method} کاوه‌نگار با وضعیت {status} ناموفق بود.";
        public static string KavenegarDefinitelyNotSubmitted(int status) =>
            $"کاوه‌نگار درخواست را پیش از ثبت، به‌دلیل خطای احراز هویت یا حساب با وضعیت {status} رد کرد.";

        public const string SmsIrTransport = "هنگام ارتباط HTTP با sms.ir خطا رخ داد.";
        public const string SmsIrTimeout = "مهلت پاسخ‌گویی sms.ir به پایان رسید.";
        public static string SmsIrHttpStatus(int statusCode) => $"sms.ir کد HTTP {statusCode} را برگرداند.";
        public static string SmsIrBadJson(string _) => "پاسخ دریافتی از sms.ir قابل خواندن نیست.";
        public const string SmsIrEmptyBody = "پاسخ دریافتی از sms.ir خالی است.";
        public const string SmsIrMissingResult = "sms.ir برای این پیام نتیجه‌ای برنگرداند.";
        public const string SmsIrNullMessageId =
            "sms.ir این گیرنده را به‌دلیل نامعتبر بودن شماره موبایل یا متن پیام رد کرد.";
        public static string SmsIrRequestStatus(string method, int status, string _) =>
            $"درخواست {method} در sms.ir با وضعیت {status} ناموفق بود.";
        public static string SmsIrRejectedStatus(int status) => $"sms.ir پیام را با وضعیت {status} رد کرد.";
        public static string SmsIrDefinitelyNotSubmitted(int status) =>
            $"sms.ir درخواست را پیش از ثبت، به‌دلیل خطای احراز هویت یا حساب با وضعیت {status} رد کرد.";

        public const string ProviderThrew = "در سرویس‌دهنده پیامک خطای غیرمنتظره‌ای رخ داد.";
    }

    public static class ProviderAccounts
    {
        public const string ProviderCodeRequired = "کد سرویس‌دهنده الزامی است.";
        public const string DisplayNameRequired = "نام نمایشی حساب الزامی است.";
        public const string AuthTypeRequired = "نوع احراز هویت الزامی است.";
        public const string SecretRequired = "اطلاعات محرمانه حساب الزامی است.";
        public const string InvalidId = "شناسه معتبر حساب سرویس‌دهنده الزامی است.";
        public const string NotFound = "حساب سرویس‌دهنده موردنظر وجود ندارد.";
        public const string UnknownProvider = "سرویس‌دهنده موردنظر وجود ندارد.";
        public const string UnsupportedProvider = "اعتبارسنجی حساب برای این سرویس‌دهنده پشتیبانی نمی‌شود.";
        public const string InvalidAuthType = "نوع احراز هویت برای این سرویس‌دهنده معتبر نیست.";
        public const string MagfaUsernameRequired = "نام کاربری باید در تنظیمات حساب مگفا وارد شود.";
        public const string MagfaDomainRequired = "دامنه باید در تنظیمات حساب مگفا وارد شود.";
    }
}
