-- Bounded, allow-listed operational diagnostics; no raw upstream payloads or credentials.
ALTER TABLE dbo.MessageBatchEvent ADD FailureJson NVARCHAR(1000) NULL;
GO
ALTER TABLE dbo.MessageBatchEvent ADD CONSTRAINT CK_MessageBatchEvent_FailureJson
    CHECK (FailureJson IS NULL OR ISJSON(FailureJson) = 1);
