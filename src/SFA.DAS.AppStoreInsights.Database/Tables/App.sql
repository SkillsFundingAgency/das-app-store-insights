CREATE TABLE [dbo].[App] (
    [Id]          INT           IDENTITY(1,1) PRIMARY KEY,
    [Name]        NVARCHAR(100) NOT NULL,
    [AppleAppId]  NVARCHAR(50)  NULL,
    [GoogleAppId] NVARCHAR(50)  NULL,
    [CreatedAt]   DATETIME2     DEFAULT GETUTCDATE()
);