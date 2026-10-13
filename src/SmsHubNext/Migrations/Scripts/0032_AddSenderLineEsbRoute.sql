-- Existing lines keep direct provider routing. ESB is an explicit per-line opt-in.
ALTER TABLE dbo.SenderLine
ADD UseEsb BIT NOT NULL
    CONSTRAINT DF_SenderLine_UseEsb DEFAULT (0) WITH VALUES;
