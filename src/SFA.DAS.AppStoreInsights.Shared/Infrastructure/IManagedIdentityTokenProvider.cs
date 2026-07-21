using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Infrastructure
{
    public interface IManagedIdentityTokenProvider
    {
        Task<string> GetSqlAccessTokenAsync();
    }
}