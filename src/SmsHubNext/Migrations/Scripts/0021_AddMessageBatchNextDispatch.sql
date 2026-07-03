-- Schedule automatic dispatch retries instead of re-claiming transient failures immediately.

ALTER TABLE dbo.MessageBatch
    ADD NextDispatchAtUtc DATETIME2(3) NULL;
GO

CREATE NONCLUSTERED INDEX IX_MessageBatch_DispatchDue
    ON dbo.MessageBatch (Status, NextDispatchAtUtc, StatusChangedAtUtc);
GO
