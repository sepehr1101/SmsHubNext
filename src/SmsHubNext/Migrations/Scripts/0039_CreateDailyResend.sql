-- Durable, once-per-day manifest. These are safety/idempotency records, not short-retention telemetry.
CREATE TABLE dbo.DailyResendOperation
(
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DailyResendOperation PRIMARY KEY,
    SubmitDateJalali CHAR(10) NOT NULL CONSTRAINT UQ_DailyResendOperation_Date UNIQUE,
    CutoffMessageId BIGINT NOT NULL,
    State TINYINT NOT NULL CONSTRAINT CK_DailyResendOperation_State CHECK (State BETWEEN 0 AND 4),
    CreatedByApiKeyId INT NOT NULL CONSTRAINT FK_DailyResendOperation_Creator REFERENCES dbo.ApiKey(Id),
    ConfirmedByApiKeyId INT NULL CONSTRAINT FK_DailyResendOperation_Confirmer REFERENCES dbo.ApiKey(Id),
    CreatedAtUtc DATETIME2(3) NOT NULL,
    ConfirmedAtUtc DATETIME2(3) NULL,
    FinishedAtUtc DATETIME2(3) NULL
);
CREATE TABLE dbo.DailyResendItem
(
    Id BIGINT IDENTITY NOT NULL CONSTRAINT PK_DailyResendItem PRIMARY KEY,
    OperationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_DailyResendItem_Operation REFERENCES dbo.DailyResendOperation(Id),
    SourceMessageId BIGINT NOT NULL CONSTRAINT UQ_DailyResendItem_Source UNIQUE,
    RequestId UNIQUEIDENTIFIER NOT NULL,
    EstimatedCost DECIMAL(19,4) NULL,
    State TINYINT NOT NULL CONSTRAINT CK_DailyResendItem_State CHECK (State BETWEEN 0 AND 2),
    SkipCode VARCHAR(80) NULL,
    NewMessageId BIGINT NULL,
    NewBatchId BIGINT NULL
);
CREATE INDEX IX_DailyResendItem_OperationState ON dbo.DailyResendItem(OperationId, State, Id);
