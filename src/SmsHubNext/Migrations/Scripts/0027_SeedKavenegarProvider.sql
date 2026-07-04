-- Add Kavenegar as the second SMS provider. Sender lines and tariffs are operational data
-- and are inserted separately per deployment/customer contract.

SET IDENTITY_INSERT dbo.Provider ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Provider WHERE Id = 2)
BEGIN
    INSERT INTO dbo.Provider (Id, Name, Code, BaseUrl, FallbackBaseUrl, IsActive)
    VALUES (2, N'Kavenegar', 'kavenegar', 'https://api.kavenegar.com', NULL, 1);
END

SET IDENTITY_INSERT dbo.Provider OFF;
GO
