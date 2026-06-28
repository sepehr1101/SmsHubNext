-- MessageBody: the 1:1 text satellite for Message (README §4.11).
-- The distinct, variable-length body is kept OFF the hot fixed-width fact to preserve
-- rows-per-page and columnstore compression. Reached only by Id point lookup, so it
-- needs neither SubmitDateJalali nor the message's partition scheme — it is partitioned
-- by Id (a monotonic identity) on its own, shorter retention schedule.

CREATE TABLE dbo.MessageBody
(
    Id   BIGINT        NOT NULL,            -- PK = FK -> Message.Id (1:1, shared key)
    Body NVARCHAR(MAX) NOT NULL,            -- the exact distinct text that was sent
    CONSTRAINT PK_MessageBody PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MessageBody_Message FOREIGN KEY (Id) REFERENCES dbo.Message (Id)
);
GO
