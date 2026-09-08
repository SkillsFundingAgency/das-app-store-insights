using SFA.DAS.AppStoreInsights.Shared.Models;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    public interface IZendeskClient
    {
        Task<string> CreateTicketAsync(ZendeskTicket ticket, CancellationToken cancellationToken = default);
    }
}