using Dapper;
using Microsoft.Data.SqlClient;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Repositories
{
    public interface IAppStoreRepository
    {
        Task<int> GetAppIdAsync(string appName, CancellationToken ct);
        Task<bool> ReviewExistsAsync(byte vendorId, string externalId, CancellationToken ct);
        Task InsertReviewAsync(Review review, CancellationToken ct);
        Task InsertUsageMetricAsync(UsageMetric metric, CancellationToken ct);
    }

    [ExcludeFromCodeCoverage]
    public class SqlAppStoreRepository : IAppStoreRepository
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly string _connectionString;

        public SqlAppStoreRepository(IConnectionFactory connectionFactory, string connectionString)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<int> GetAppIdAsync(string appName, CancellationToken ct)
        {
            using var conn = _connectionFactory.CreateConnection(_connectionString);
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT Id FROM [dbo].[App] WHERE Name = @name",
                    new { name = appName },
                    commandType: CommandType.Text,
                    cancellationToken: ct));
        }

        public async Task<bool> ReviewExistsAsync(byte vendorId, string externalId, CancellationToken ct)
        {
            using var conn = _connectionFactory.CreateConnection(_connectionString);
            var count = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(1) FROM [dbo].[Review] WHERE VendorId = @vendorId AND ExternalId = @externalId",
                    new { vendorId, externalId },
                    commandType: CommandType.Text,
                    cancellationToken: ct));
            return count > 0;
        }

        public async Task InsertReviewAsync(Review review, CancellationToken ct)
        {
            const string sql = @"
                INSERT INTO [dbo].[Review] (AppId, VendorId, ExternalId, ReviewerName, Rating, Title, Comment, ReviewDate, DeviceInfo, IsNegative)
                VALUES (@AppId, @VendorId, @ExternalId, @ReviewerName, @Rating, @Title, @Comment, @ReviewDate, @DeviceInfo, @IsNegative)";

            using var conn = _connectionFactory.CreateConnection(_connectionString);
            await conn.ExecuteAsync(
                new CommandDefinition(sql, review, cancellationToken: ct));
        }

        public async Task InsertUsageMetricAsync(UsageMetric metric, CancellationToken ct)
        {
            const string sql = @"
            MERGE INTO [dbo].[UsageMetric] AS target
            USING (SELECT @AppId AS AppId, @VendorId AS VendorId, @MetricDate AS MetricDate) AS source
            ON target.AppId = source.AppId 
               AND target.VendorId = source.VendorId 
               AND target.MetricDate = source.MetricDate
            WHEN MATCHED THEN
                UPDATE SET 
                    Downloads = @Downloads,
                    ActiveUsers = @ActiveUsers,
                    CreatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (AppId, VendorId, MetricDate, Downloads, ActiveUsers)
                VALUES (@AppId, @VendorId, @MetricDate, @Downloads, @ActiveUsers);";

            using var conn = _connectionFactory.CreateConnection(_connectionString);
            await conn.ExecuteAsync(
                new CommandDefinition(sql, metric, cancellationToken: ct));
        }
    }
}