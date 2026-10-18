-- Seek one batch's messages in bounded Id pages without scanning the fact table.
-- Leading Jalali date keeps the access path compatible with partition alignment.
-- Deliberately key-only: send/delivery statuses are not included, so their hot-row
-- updates do not maintain this index. At most one page needs clustered lookups.
CREATE NONCLUSTERED INDEX IX_Message_BatchBrowse
    ON dbo.Message (SubmitDateJalali, MessageBatchId, Id);
