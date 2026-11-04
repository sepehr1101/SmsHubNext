CREATE TABLE dbo.FrequentlyAskedQuestion
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FrequentlyAskedQuestion PRIMARY KEY,
    Question NVARCHAR(500) NOT NULL,
    Answer NVARCHAR(4000) NOT NULL,
    SortOrder INT NOT NULL,
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Faq_Updated DEFAULT SYSUTCDATETIME(),
    UpdatedByApiKeyId INT NULL CONSTRAINT FK_Faq_ApiKey REFERENCES dbo.ApiKey(Id)
);
INSERT dbo.FrequentlyAskedQuestion (Question, Answer, SortOrder) VALUES
(N'آیا می‌توان از نرم‌افزار سازمان پیامک ارسال کرد؟', N'بله، نرم‌افزار سازمان می‌تواند از طریق API و با کلید دسترسی مشتری، درخواست ارسال ثبت کند.', 1),
(N'گزارش‌ها بر اساس چه تقویم و واحد پولی هستند؟', N'گزارش‌های دوره‌ای بر اساس تقویم شمسی و مبلغ‌ها به ریال نمایش داده می‌شوند.', 2),
(N'آیا وضعیت ارسال و تحویل یکسان است؟', N'خیر. پذیرش پیام توسط ارائه‌دهنده و تحویل آن به گیرنده دو مرحله متفاوت‌اند؛ وضعیت تحویل و تاریخچه آن جداگانه قابل پیگیری است.', 3);
