-- Operational monitoring only; does not change business tables or their indexes.
CREATE TABLE dbo.DatabaseTableSnapshot
(
    Id BIGINT IDENTITY NOT NULL CONSTRAINT PK_DatabaseTableSnapshot PRIMARY KEY,
    SnapshotDateUtc DATE NOT NULL,
    CapturedAtUtc DATETIME2(3) NOT NULL,
    ObjectId INT NOT NULL,
    TableName NVARCHAR(257) NOT NULL,
    ApproximateRows BIGINT NOT NULL,
    DataMb DECIMAL(19,4) NOT NULL,
    IndexMb DECIMAL(19,4) NOT NULL,
    CONSTRAINT UQ_DatabaseTableSnapshot_DayObject UNIQUE (SnapshotDateUtc, ObjectId)
);
CREATE TABLE dbo.DatabaseIndexObservation
(
    Id BIGINT IDENTITY NOT NULL CONSTRAINT PK_DatabaseIndexObservation PRIMARY KEY,
    ObjectId INT NOT NULL,
    IndexId INT NOT NULL,
    StartedAtServerTime DATETIME2(3) NOT NULL,
    IndexName NVARCHAR(128) NOT NULL,
    TableName NVARCHAR(257) NOT NULL,
    FirstObservedAtUtc DATETIME2(3) NOT NULL,
    LastObservedAtUtc DATETIME2(3) NOT NULL,
    StateDescription NVARCHAR(60) NOT NULL,
    PercentComplete DECIMAL(9,4) NOT NULL,
    ExecutionMinutes INT NOT NULL,
    CONSTRAINT UQ_DatabaseIndexObservation_Operation UNIQUE (ObjectId, IndexId, StartedAtServerTime)
);

