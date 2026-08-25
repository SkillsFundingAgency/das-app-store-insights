CREATE TABLE [dbo].[UsageMetric] (
    [Id]          BIGINT        IDENTITY(1,1) PRIMARY KEY,
    [AppId]       INT           NOT NULL REFERENCES [dbo].[App](Id),
    [VendorId]    TINYINT       NOT NULL REFERENCES [dbo].[Vendor](Id),
    [MetricDate]  DATE          NOT NULL,
    [Downloads]   INT           NOT NULL DEFAULT 0,
    [ActiveUsers] INT           NULL,
    [CreatedAt]   DATETIME2     DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_UsageMetric UNIQUE (AppId, VendorId, MetricDate)
);
GO

CREATE INDEX IX_UsageMetric_MetricDate ON [dbo].[UsageMetric](MetricDate);