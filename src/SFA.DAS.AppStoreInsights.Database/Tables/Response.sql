CREATE TABLE [dbo].[Response] (
    [Id]               BIGINT        IDENTITY(1,1) PRIMARY KEY,
    [ReviewId]         BIGINT        NOT NULL,
    [ResponderType]    NVARCHAR(50)  NOT NULL,
    [ResponseText]     NVARCHAR(MAX) NOT NULL,
    [ResponseDate]     DATETIME2     NOT NULL,
    [ExternalResponseId] NVARCHAR(100) NULL,
    [CreatedAt]        DATETIME2     DEFAULT GETUTCDATE()
);