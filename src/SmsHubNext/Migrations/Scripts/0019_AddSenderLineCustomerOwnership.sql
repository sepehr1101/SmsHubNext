-- Additive sender-line ownership: shared lines remain available to all customers; a dedicated
-- line can be bound to one customer. Existing dedicated lines stay unbound until configured.

ALTER TABLE dbo.SenderLine
    ADD CustomerId SMALLINT NULL;
GO

ALTER TABLE dbo.SenderLine
    ADD CONSTRAINT FK_SenderLine_Customer
    FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id);
GO

CREATE NONCLUSTERED INDEX IX_SenderLine_Customer ON dbo.SenderLine (CustomerId)
    WHERE CustomerId IS NOT NULL;
GO
