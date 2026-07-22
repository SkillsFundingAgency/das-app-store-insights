SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM [dbo].[Vendor] WHERE Id = 1)
    INSERT INTO [dbo].[Vendor] (Id, Name) VALUES (1, 'Apple');

IF NOT EXISTS (SELECT 1 FROM [dbo].[Vendor] WHERE Id = 2)
    INSERT INTO [dbo].[Vendor] (Id, Name) VALUES (2, 'Google');

IF NOT EXISTS (SELECT 1 FROM [dbo].[App] WHERE Name = 'Apprentice App')
BEGIN
    INSERT INTO [dbo].[App] (Name, AppleAppId, GoogleAppId)
    VALUES (
        'Apprentice App',
        'uk.gov.education.apprenticeships.myapprenticeship',
        'uk.gov.education.myapprenticeship'
    );
END

DECLARE @AppId INT = (SELECT Id FROM [dbo].[App] WHERE Name = 'Apprentice App');

MERGE INTO [dbo].[Review] AS target
USING (
    VALUES
        (1, @AppId, 'apple_neg_001', 'ChrisH', 1, 'Crashes constantly', 'App won''t open – unusable', DATEADD(day, -2, GETUTCDATE()), 'iPhone 14 Pro / iOS 17.2', 1, NULL, NULL, NULL),
        (1, @AppId, 'apple_pos_001', 'GemmaW', 5, 'Great app', 'Really helps me track my apprenticeship', DATEADD(day, -3, GETUTCDATE()), 'iPhone 13 / iOS 17.2', 0, NULL, NULL, NULL),
        (1, @AppId, 'apple_neg_004', 'RachelM', 2, 'Disappointed', 'Had to call support multiple times', DATEADD(day, -8, GETUTCDATE()), 'iPhone 12 / iOS 17.1', 1, '12345', DATEADD(day, -7, GETUTCDATE()), DATEADD(day, -7, GETUTCDATE()))
) AS source (
    VendorId, AppId, ExternalId, ReviewerName, Rating, Comment, Title, ReviewDate, DeviceInfo, IsNegative, ZendeskTicketId, ProcessedAt, UpdatedAt
)
ON target.VendorId = source.VendorId AND target.ExternalId = source.ExternalId
WHEN NOT MATCHED THEN
    INSERT (VendorId, AppId, ExternalId, ReviewerName, Rating, Comment, Title, ReviewDate, DeviceInfo, IsNegative, ZendeskTicketId, ProcessedAt, UpdatedAt)
    VALUES (source.VendorId, source.AppId, source.ExternalId, source.ReviewerName, source.Rating, source.Comment, source.Title, source.ReviewDate, source.DeviceInfo, source.IsNegative, source.ZendeskTicketId, source.ProcessedAt, source.UpdatedAt);

MERGE INTO [dbo].[Review] AS target
USING (
    VALUES
        (2, @AppId, 'google_neg_001', 'ConnorM', 1, 'Keeps crashing', 'App crashes on launch', DATEADD(day, -1, GETUTCDATE()), 'Google Pixel 7 / Android 14', 1, NULL, NULL, NULL),
        (2, @AppId, 'google_pos_001', 'NadiaK', 5, 'Perfect', 'Everything works well', DATEADD(day, -2, GETUTCDATE()), 'Samsung Galaxy S24 / Android 14', 0, NULL, NULL, NULL),
        (2, @AppId, 'google_neg_004', 'SusanT', 2, 'Needs improvement', 'Been reporting issues for weeks', DATEADD(day, -10, GETUTCDATE()), 'Google Pixel 6 / Android 13', 1, '12347', DATEADD(day, -9, GETUTCDATE()), DATEADD(day, -9, GETUTCDATE()))
) AS source (
    VendorId, AppId, ExternalId, ReviewerName, Rating, Comment, Title, ReviewDate, DeviceInfo, IsNegative, ZendeskTicketId, ProcessedAt, UpdatedAt
)
ON target.VendorId = source.VendorId AND target.ExternalId = source.ExternalId
WHEN NOT MATCHED THEN
    INSERT (VendorId, AppId, ExternalId, ReviewerName, Rating, Comment, Title, ReviewDate, DeviceInfo, IsNegative, ZendeskTicketId, ProcessedAt, UpdatedAt)
    VALUES (source.VendorId, source.AppId, source.ExternalId, source.ReviewerName, source.Rating, source.Comment, source.Title, source.ReviewDate, source.DeviceInfo, source.IsNegative, source.ZendeskTicketId, source.ProcessedAt, source.UpdatedAt);

MERGE INTO [dbo].[UsageMetric] AS target
USING (
    VALUES
        (@AppId, 1, DATEADD(day, -1, GETUTCDATE()), 1250, 980, NULL, NULL, '{"Date":"2024-06-10","Downloads":1250,"Installs":980,"Uninstalls":0,"Sessions":0,"DailyActiveDevices":0}'),
        (@AppId, 1, DATEADD(day, -2, GETUTCDATE()), 1100, 850, NULL, NULL, '{"Date":"2024-06-09","Downloads":1100,"Installs":850,"Uninstalls":0,"Sessions":0,"DailyActiveDevices":0}'),
        (@AppId, 1, DATEADD(day, -3, GETUTCDATE()), 1350, 1020, NULL, NULL, '{"Date":"2024-06-08","Downloads":1350,"Installs":1020,"Uninstalls":0,"Sessions":0,"DailyActiveDevices":0}')
) AS source (
    AppId, VendorId, MetricDate, Downloads, Installs, Uninstalls, Sessions, RawDataJson
)
ON target.AppId = source.AppId 
    AND target.VendorId = source.VendorId 
    AND target.MetricDate = source.MetricDate
WHEN NOT MATCHED THEN
    INSERT (AppId, VendorId, MetricDate, Downloads, Installs, Uninstalls, Sessions, RawDataJson)
    VALUES (source.AppId, source.VendorId, source.MetricDate, source.Downloads, source.Installs, source.Uninstalls, source.Sessions, source.RawDataJson);

MERGE INTO [dbo].[UsageMetric] AS target
USING (
    VALUES
        (@AppId, 2, DATEADD(day, -1, GETUTCDATE()), 980, 850, 45, 3200, '{"Date":"2024-06-10","Downloads":980,"Installs":850,"Uninstalls":45,"DailyActiveUsers":0,"Sessions":3200}'),
        (@AppId, 2, DATEADD(day, -2, GETUTCDATE()), 870, 740, 38, 2900, '{"Date":"2024-06-09","Downloads":870,"Installs":740,"Uninstalls":38,"DailyActiveUsers":0,"Sessions":2900}'),
        (@AppId, 2, DATEADD(day, -3, GETUTCDATE()), 1050, 920, 52, 3500, '{"Date":"2024-06-08","Downloads":1050,"Installs":920,"Uninstalls":52,"DailyActiveUsers":0,"Sessions":3500}')
) AS source (
    AppId, VendorId, MetricDate, Downloads, Installs, Uninstalls, Sessions, RawDataJson
)
ON target.AppId = source.AppId 
    AND target.VendorId = source.VendorId 
    AND target.MetricDate = source.MetricDate
WHEN NOT MATCHED THEN
    INSERT (AppId, VendorId, MetricDate, Downloads, Installs, Uninstalls, Sessions, RawDataJson)
    VALUES (source.AppId, source.VendorId, source.MetricDate, source.Downloads, source.Installs, source.Uninstalls, source.Sessions, source.RawDataJson);

INSERT INTO [dbo].[Response] (ReviewId, ResponderType, ResponseText, ResponseDate, ExternalResponseId, CreatedAt)
SELECT
    r.Id,
    'SupportAgent',
    rd.ResponseText,
    rd.ResponseDate,
    NULL,
    rd.ResponseDate
FROM [dbo].[Review] r
INNER JOIN (
    VALUES
        ('apple_neg_004', 'Thank you for your feedback. We''ve looked into the issue and released a fix in the latest update. Please let us know if you still experience problems.', DATEADD(day, -7, GETUTCDATE())),
        ('google_neg_004', 'We appreciate your feedback. We''ve identified the issue and are rolling out a fix in the next release. Thank you for your patience.', DATEADD(day, -9, GETUTCDATE()))
) AS rd (ExternalId, ResponseText, ResponseDate)
ON r.ExternalId = rd.ExternalId
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[Response] resp
    WHERE resp.ReviewId = r.Id
);