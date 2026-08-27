## ⛔Never push sensitive information such as client id's, secrets or keys into repositories including in the README file⛔

# App Store Insights

<img src="https://avatars.githubusercontent.com/u/9841374?s=200&v=4" align="right" alt="UK Government logo">

[![Build Status](https://dev.azure.com/sfa-gov-uk/Digital%20Apprenticeship%20Service/_apis/build/status/4393?branchName=main)](https://dev.azure.com/sfa-gov-uk/Digital%20Apprenticeship%20Service/_build?definitionId=4393)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=SkillsFundingAgency_das-app-store-insights&metric=alert_status)](https://sonarcloud.io/dashboard?id=SkillsFundingAgency_das-app-store-insights)
[![Jira Project](https://img.shields.io/badge/Jira-Project-blue)](https://skillsfundingagency.atlassian.net/browse/DASD)
[![Confluence Project](https://img.shields.io/badge/Confluence-Project-blue)](https://skillsfundingagency.atlassian.net/wiki) <!-- TODO: replace with the actual App Store Insights Confluence space link -->
[![License](https://img.shields.io/badge/license-MIT-lightgrey.svg?longCache=true&style=flat-square)](https://en.wikipedia.org/wiki/MIT_License)

App Store Insights collects data about the Apprentice App from the Apple App Store and Google Play Store and stores it centrally in a SQL database, so that reviews, ratings and usage figures can be reported on without going to each store individually.

1. Ingests app store reviews and ratings from Apple App Store Connect and Google Play daily
2. Ingests app usage metrics (downloads and active users) from Apple App Store Connect and Google Play daily
3. Deduplicates reviews already stored before inserting new ones
4. Stores everything in a shared SQL database, keyed by app and vendor (Apple / Google)

## How It Works

The solution is made up of two Azure Function Apps that share a common `SFA.DAS.AppStoreInsights.Shared` class library and a single SQL database:

* **Feedback.Functions** - runs `FetchAppStoreFeedback` on a daily timer trigger. It calls the Apple and Google clients for reviews, checks whether each review already exists in the database (by vendor and external review id) and inserts any new ones into the `Reviews` table.
* **UsageMetrics.Functions** - runs two daily timer-triggered functions, `FetchAppleUsageMetrics` (7 day rolling window) and `FetchGoogleUsageMetrics` (30 day rolling window), which pull download and active user figures and insert them into the `UsageMetric` table.

Both function apps resolve their dependencies through `AddAppStoreInsightsHost` in the Shared project, which wires up:

* `IAppleStoreClient` / `IGooglePlayClient` - typed HTTP clients that call the Apple App Store Connect API and Google Play Developer API
* `IAppStoreRepository` (`SqlAppStoreRepository`) - Dapper-based repository that reads/writes the shared SQL database
* Configuration loaded from Azure Table Storage via `SFA.DAS.Configuration.AzureTableStorage`, using a `ConfigNames` value that is specific to each function app (`SFA.DAS.AppStoreInsights.Feedback.Functions` / `SFA.DAS.AppStoreInsights.UsageMetrics.Functions`)

Both function apps are deployed into the same resource group and share the same App Service Plan, VNet subnet and SQL firewall rule, each with its own Application Insights instance for separate telemetry.

## 🚀 Installation

### Pre-Requisites

* A clone of this repository
* An IDE that supports .NET 10 and Azure Functions (Visual Studio 2022 17.12+, or VS Code with the Azure Functions extension and Azure Functions Core Tools v4)
* Azure Storage Emulator or [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) for local `AzureWebJobsStorage` / `ConfigurationStorageConnectionString`
* SQL Server (LocalDB is sufficient) with the schema from `src/SFA.DAS.AppStoreInsights.Database` deployed
* An Apple App Store Connect API key with access to Ratings & Reviews and App Analytics (private key, issuer id, key id, vendor number)
* A Google Play Developer service account with access to the Google Play Android Publisher API and the Play Console usage stats bucket

### Config

Configuration for both function apps is held in the [das-employer-config repository](https://github.com/SkillsFundingAgency/das-employer-config/tree/master/das-app-store-insights), which holds one schema/config file per function app:

* `SFA.DAS.AppStoreInsights.Feedback.Functions.json` / `.schema.json`
* `SFA.DAS.AppStoreInsights.UsageMetrics.Functions.json` / `.schema.json`

For local development, add a `local.settings.json` file to each function app project, for example:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ConfigurationStorageConnectionString": "UseDevelopmentStorage=true;",
    "ConfigNames": "SFA.DAS.AppStoreInsights.Feedback.Functions",
    "EnvironmentName": "LOCAL",
    "Version": "1.0",
    "SqlConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=AppStoreInsights;Trusted_Connection=True;"
  }
}
```

> Use `ConfigNames: "SFA.DAS.AppStoreInsights.UsageMetrics.Functions"` in the UsageMetrics.Functions project instead.

The corresponding Azure Table Storage config row (partition key `LOCAL`) needs an `ApplicationConfiguration` section (`AppleAppId`, `GooglePackageName`, and `GoogleStatsBucketName` for UsageMetrics.Functions), plus `Apple` (`PrivateKey`, `IssuerId`, `KeyId`, `VendorNumber`) and `Google` (`ServiceAccountJson`, `StatsBucketName`) sections - see the config files linked above for the full shape.

## 🔗 External Dependencies

* [Apple App Store Connect API](https://developer.apple.com/documentation/appstoreconnectapi) - Ratings & Reviews and App Analytics endpoints
* [Google Play Developer API](https://developers.google.com/android-publisher) - Android Publisher API, plus the Play Console usage stats Cloud Storage bucket
* A shared Azure SQL Database, deployed via the `SFA.DAS.AppStoreInsights.Database` DACPAC as part of this pipeline

## Technologies

* .NET 10 / C#
* Azure Functions v4 (isolated worker model)
* Azure SQL Database with Dapper
* Azure Table Storage (`SFA.DAS.Configuration.AzureTableStorage`)
* MediatR, FluentValidation
* Google APIs (`Google.Apis.AndroidPublisher`, `Google.Apis.Auth`, `Google.Cloud.Storage.V1`)
* Apple App Store Connect API, using JWT signing via BouncyCastle
* NUnit, Moq, FluentAssertions, AutoFixture for unit tests
* Reqnroll for BDD-style acceptance tests
* Azure DevOps YAML pipelines and ARM templates ([das-platform-building-blocks](https://github.com/SkillsFundingAgency/das-platform-building-blocks))

## 🐛 Known Issues

No known issues at this time.
