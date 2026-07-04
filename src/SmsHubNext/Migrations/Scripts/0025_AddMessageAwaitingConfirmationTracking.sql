-- Track submit outcomes that are unknown after a provider handoff. A message in
-- AwaitingConfirmation must not be resent until enough time and negative provider
-- lookups make the resend safe.

ALTER TABLE dbo.Message
    ADD AwaitingConfirmationSinceUtc DATETIME2(3) NULL,
        ConfirmationLookupCount INT NOT NULL
            CONSTRAINT DF_Message_ConfirmationLookupCount DEFAULT (0);
