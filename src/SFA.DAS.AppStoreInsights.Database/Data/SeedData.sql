-- App seed
IF NOT EXISTS (SELECT 1 FROM [dbo].[App] WHERE Name = 'Apprentice App')
BEGIN
    INSERT INTO [dbo].[App] (Name, AppleAppId, GoogleAppId)
    VALUES ('Apprentice App', 'uk.gov.education.apprenticeships.myapprenticeship', 'uk.gov.education.myapprenticeship');
END

-- Vendor seeds
IF NOT EXISTS (SELECT 1 FROM [dbo].[Vendor] WHERE Id = 1)
    INSERT INTO [dbo].[Vendor] (Id, Name) VALUES (1, 'Apple');

IF NOT EXISTS (SELECT 1 FROM [dbo].[Vendor] WHERE Id = 2)
    INSERT INTO [dbo].[Vendor] (Id, Name) VALUES (2, 'Google');