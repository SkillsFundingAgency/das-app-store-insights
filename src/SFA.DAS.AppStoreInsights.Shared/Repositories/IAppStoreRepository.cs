using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Models;
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
        Task<IEnumerable<Review>> GetUnprocessedNegativeReviewsAsync(int appId, CancellationToken ct);
        Task InsertResponseAsync(long reviewId, string responseText, string responder, DateTime respondedAt, CancellationToken ct);
    }

    [ExcludeFromCodeCoverage]
    public class SqlAppStoreRepository : IAppStoreRepository
    {
        private readonly string _connectionString;

        public SqlAppStoreRepository(IConfiguration config)
        {
            _connectionString = config["SqlConnectionString"]
                ?? throw new InvalidOperationException("SqlConnectionString not found in configuration");
        }

        public async Task<int> GetAppIdAsync(string appName, CancellationToken ct)
        {
            using var conn = new SqlConnection(_connectionString);
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT Id FROM [dbo].[App] WHERE Name = @name",
                    new { name = appName },
                    commandType: CommandType.Text,
                    cancellationToken: ct));
        }

        public async Task<bool> ReviewExistsAsync(byte vendorId, string externalId, CancellationToken ct)
        {
            using var conn = new SqlConnection(_connectionString);
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

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(
                new CommandDefinition(sql, review, cancellationToken: ct));
        }

        public async Task<IEnumerable<Review>> GetUnprocessedNegativeReviewsAsync(int appId, CancellationToken ct)
        {
            const string sql = @"
                SELECT Id, AppId, VendorId, ExternalId, ReviewerName, Rating, Title, Comment, ReviewDate, DeviceInfo, IsNegative, ZendeskTicketId
                FROM [dbo].[Review] 
                WHERE AppId = @appId 
                    AND IsNegative = 1 
                    AND (ZendeskTicketId IS NULL OR ZendeskTicketId = '')
                    AND ProcessedAt IS NULL
                ORDER BY ReviewDate DESC";

            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<Review>(
                new CommandDefinition(sql, new { appId }, cancellationToken: ct));
        }
        public async Task InsertResponseAsync(long reviewId, string responseText, string responder, DateTime respondedAt, CancellationToken ct)
        {
            const string sql = @"
                    INSERT INTO [dbo].[Response] (ReviewId, ResponderType, ResponseText, ResponseDate, CreatedAt)
                    VALUES (@reviewId, @responderType, @responseText, @responseDate, @createdAt)";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    reviewId,
                    responderType = responder,
                    responseText,
                    responseDate = respondedAt,
                    createdAt = DateTime.UtcNow
                }, cancellationToken: ct));
        }
    }
}