using Microsoft.Data.SqlClient;

namespace SFA.DAS.AppStoreInsights.Shared.Infrastructure
{
    public interface IConnectionFactory
    {
        SqlConnection CreateConnection(string connectionString);
    }
}