-- Nullable without WITH VALUES: avoid rewriting historical fact partitions.
-- Existing rows use the batch attempt count conservatively until their next send.
ALTER TABLE dbo.Message ADD
    SendAttemptCount INT NULL CONSTRAINT DF_Message_SendAttemptCount DEFAULT (0),
    FirstSendAttemptAtUtc DATETIME2(3) NULL;
