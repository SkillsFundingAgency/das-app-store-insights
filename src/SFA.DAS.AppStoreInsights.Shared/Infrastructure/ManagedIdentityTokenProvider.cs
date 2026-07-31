using Azure.Core;
using Azure.Identity;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Infrastructure
{
    [ExcludeFromCodeCoverage]
    public class ManagedIdentityTokenProvider : IManagedIdentityTokenProvider
    {
        private readonly DefaultAzureCredential _credential;

        public ManagedIdentityTokenProvider()
        {
            _credential = new DefaultAzureCredential();
        }

        public async Task<string> GetSqlAccessTokenAsync()
        {
            var tokenRequestContext = new TokenRequestContext(
                new[] { "https://database.windows.net/.default" }
            );
            var token = await _credential.GetTokenAsync(tokenRequestContext);
            return token.Token;
        }
    }
}