CREATE TABLE [dbo].[Review] (
    [Id]              BIGINT        IDENTITY(1,1) PRIMARY KEY,
    [AppId]           INT           NOT NULL REFERENCES [dbo].[App](Id),
    [VendorId]        TINYINT       NOT NULL REFERENCES [dbo].[Vendor](Id),
    [ExternalId]      NVARCHAR(100) NOT NULL,
    [ReviewerName]    NVARCHAR(200) NULL,
    [Rating]          TINYINT       NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    [Title]           NVARCHAR(500) NULL,
    [Comment]         NVARCHAR(MAX) NULL,
    [ReviewDate]      DATETIME2     NOT NULL,
    [DeviceInfo]      NVARCHAR(500) NULL,
    [IsNegative]      BIT           NOT NULL DEFAULT 0,
    [ZendeskTicketId] NVARCHAR(50)  NULL,
    [CreatedAt]       DATETIME2     DEFAULT GETUTCDATE(),
    [ProcessedAt]     DATETIME2     NULL,
    [UpdatedAt]       DATETIME2     NULL,    
    CONSTRAINT UQ_Review_ExternalId UNIQUE (VendorId, ExternalId)
);
GO

CREATE INDEX IX_Review_ReviewDate ON [dbo].[Review](ReviewDate);
GO

CREATE INDEX IX_Review_IsNegative_Zendesk ON [dbo].[Review](IsNegative, ZendeskTicketId) WHERE ZendeskTicketId IS NULL;