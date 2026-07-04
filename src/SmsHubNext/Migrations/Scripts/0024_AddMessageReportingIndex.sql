-- Practical rowstore support for statistical reports until monthly partition/filegroup
-- management is introduced. Keeps filters seekable and covers the aggregate columns.

CREATE NONCLUSTERED INDEX IX_Message_ReportFilters
    ON dbo.Message (SubmitDateJalali, CustomerId, ProviderId, MessageTypeId, GeoSectionId)
    INCLUDE (SegmentCount, TotalCost, DeliveryStatus);
