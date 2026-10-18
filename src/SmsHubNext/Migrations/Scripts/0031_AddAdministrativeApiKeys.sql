-- Administrative API keys are deliberately distinct from customer send keys.
-- They remain in ApiKey so the existing audit foreign keys continue to identify
-- the exact credential that performed each management mutation.

ALTER TABLE dbo.Customer ADD
    IsSystem BIT NOT NULL
        CONSTRAINT DF_Customer_IsSystem DEFAULT (0);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_SingleSystemCustomer
    ON dbo.Customer (IsSystem)
    WHERE IsSystem = 1;
GO

ALTER TABLE dbo.ApiKey ADD
    AccessLevel TINYINT NOT NULL
        CONSTRAINT DF_ApiKey_AccessLevel DEFAULT (0);
GO

ALTER TABLE dbo.ApiKey ADD
    CONSTRAINT CK_ApiKey_AccessLevel CHECK (AccessLevel IN (0, 1));
GO

CREATE NONCLUSTERED INDEX IX_ApiKey_AccessLevel
    ON dbo.ApiKey (AccessLevel, IsActive)
    INCLUDE (CustomerId, ExpiresAtUtc, RevokedAtUtc);
GO
