using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    public interface IClientSyncConfigProvider
    {
        Task<bool> IsClientActiveAsync(string clientId);
        Task<IEnumerable<string>> GetEnabledResourcesAsync(string clientId);
    }
}