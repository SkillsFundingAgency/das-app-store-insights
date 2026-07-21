using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.Shared.Infrastructure
{
    [ExcludeFromCodeCoverage]
    public class SqlServerConnectionFactory : IConnectionFactory
    {
        private readonly IConfiguration _configuration;
        private readonly IManagedIdentityTokenProvider _tokenProvider;

        public SqlServerConnectionFactory(IConfiguration configuration, IManagedIdentityTokenProvider tokenProvider)
        {
            _configuration = configuration;
            _tokenProvider = tokenProvider;
        }

        public SqlConnection CreateConnection(string connectionString)
        {
            var environment = _configuration["EnvironmentName"] ?? "LOCAL";
            bool useManagedIdentity = !IsLocalEnvironment(environment);

            var connection = new SqlConnection(connectionString);

            if (useManagedIdentity)
            {
                var token = _tokenProvider.GetSqlAccessTokenAsync().GetAwaiter().GetResult();
                connection.AccessToken = token;
            }

            return connection;
        }

        private static bool IsLocalEnvironment(string environment)
        {
            return environment.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) ||
                   environment.Equals("ACCEPTANCE_TESTS", StringComparison.OrdinalIgnoreCase) ||
                   environment.Equals("DEV", StringComparison.OrdinalIgnoreCase);
        }
    }
}